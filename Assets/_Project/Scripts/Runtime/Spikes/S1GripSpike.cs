using System;
using System.Collections.Generic;
using System.Text;
using ScootFoma.Presentation;
using ScootFoma.Rig;
using ScootFoma.Simulation;
using ScootFoma.Wheel;
using UnityEngine;

namespace ScootFoma.Spikes
{
    /// Спайк S1: держат ли Joint-хваты райдера (ArticulationBody, ~77 кг) на руле (Rigidbody, ~4 кг).
    /// Прогоняет матрицу «конфигурация × сценарий» и пишет метрики в консоль с префиксом [S1].
    public sealed class S1GripSpike : MonoBehaviour, ISimulationStep
    {
        public enum Scenario { Idle, Spin, Flip, Drop }

        [Serializable]
        public class Config
        {
            public string name = "base";
            public float hz = 120f;
            public float gripMassScale = 1f;
            public float scooterMassMultiplier = 1f;
            public bool gravityCompensation = true;
            [Tooltip("<0 — взять из coupling.")]
            public float footMaxForce = -1f;
            public bool grips = true;
            [Tooltip("Гироскопический момент для Rigidbody самоката.")]
            public bool gyroscopic = true;
        }

        [SerializeField] SimulationLoop loop;
        [SerializeField] PoseInterpolator interpolator;
        [SerializeField] FollowCamera followCamera;
        [SerializeField] Material scooterMaterial;
        [SerializeField] Material riderMaterial;
        [Tooltip("Ускорение прогона матрицы. После окончания скорость возвращается к 1.")]
        [SerializeField] float runSpeed = 4f;
        [Tooltip("Писать разложение момента импульса в сценариях вращения.")]
        [SerializeField] bool logMomentum = true;

        [SerializeField] List<Config> configs = new List<Config>
        {
            new Config { name = "120Hz" },
            new Config { name = "240Hz", hz = 240f },
            new Config { name = "120Hz massScale4", gripMassScale = 4f },
            new Config { name = "120Hz scooter x2", scooterMassMultiplier = 2f },
            new Config { name = "120Hz noGravComp", gravityCompensation = false },
        };
        [SerializeField] Scenario[] scenarios = { Scenario.Idle, Scenario.Spin, Scenario.Flip, Scenario.Drop };

        public RiderBuildSettings rider = new RiderBuildSettings();
        public ScooterBuildSettings scooter = new ScooterBuildSettings { stabilizers = true };
        public CouplingSettings coupling = new CouplingSettings();
        public WheelFrictionTuning wheels = new WheelFrictionTuning();

        struct Metrics
        {
            public float MaxGrip, SumGrip, MaxGripSettled, MaxFoot, MaxSpeed, MaxDeltaL, PelvisSag;
            public int Samples;
            public bool Exploded;
        }

        SpikeRig rig;
        int runIndex = -1;
        float runTime;
        bool pendingInitialMotion;
        Vector3 initialL;
        float initialPelvisHeight;
        Metrics m;
        readonly List<string> results = new List<string>();
        bool finished;

        public int Order => SimulationOrder.Sensing;

        Config CurrentConfig => configs[runIndex / scenarios.Length];
        Scenario CurrentScenario => scenarios[runIndex % scenarios.Length];
        int RunCount => configs.Count * scenarios.Length;

        static float Duration(Scenario s) => s == Scenario.Idle ? 4f : s == Scenario.Drop ? 2.5f : 1.5f;
        static float SettleTime(Scenario s) => s == Scenario.Idle ? 1.5f : 0f;

        void Start()
        {
            if (loop == null) loop = FindAnyObjectByType<SimulationLoop>();
            if (interpolator == null) interpolator = FindAnyObjectByType<PoseInterpolator>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            loop.Register(this);
            loop.Speed = runSpeed;
            loop.MaxTicksPerFrame = Mathf.CeilToInt(16 * runSpeed);
            results.Add($"[S1] solver={SolverName()} конфигураций={configs.Count} сценариев={scenarios.Length}");
            NextRun();
        }

        void OnDisable() { if (loop != null) loop.Unregister(this); }

        /// Тип решателя недоступен из runtime API; в редакторе читаем его из настроек проекта.
        static string SolverName()
        {
#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (assets.Length > 0)
            {
                var prop = new UnityEditor.SerializedObject(assets[0]).FindProperty("m_SolverType");
                if (prop != null) return prop.enumNames[prop.enumValueIndex];
            }
#endif
            return "unknown";
        }

        void NextRun()
        {
            rig?.Destroy();
            rig = null;
            runIndex++;
            if (runIndex >= RunCount)
            {
                Finish();
                return;
            }

            var cfg = CurrentConfig;
            var sc = CurrentScenario;
            loop.SetPhysicsHz(cfg.hz);

            var s = JsonUtility.FromJson<ScooterBuildSettings>(JsonUtility.ToJson(scooter));
            s.deckMass *= cfg.scooterMassMultiplier;
            s.barMass *= cfg.scooterMassMultiplier;
            s.stabilizers = sc == Scenario.Idle || sc == Scenario.Drop;

            var c = JsonUtility.FromJson<CouplingSettings>(JsonUtility.ToJson(coupling));
            c.gripMassScale = cfg.gripMassScale;
            c.gravityCompensation = cfg.gravityCompensation && (sc == Scenario.Idle || sc == Scenario.Drop);
            if (cfg.footMaxForce >= 0f) c.footMaxForce = cfg.footMaxForce;
            c.grips = cfg.grips;

            float height = sc == Scenario.Idle ? 0f : sc == Scenario.Drop ? 1.5f : 20f;
            var pose = new Pose(new Vector3(0f, height, 0f), Quaternion.identity);
            rig = SpikeRig.Build($"S1 {cfg.name} {sc}", pose, loop, interpolator, s, wheels, rider, c, scooterMaterial, riderMaterial, cfg.gyroscopic);
            if (followCamera != null) followCamera.Target = rig.Focus;

            runTime = 0f;
            m = default;
            pendingInitialMotion = true;
            initialPelvisHeight = rig.Rider.Pelvis.transform.position.y - rig.Scooter.Deck.position.y;
        }

        public void PrePhysics(float dt)
        {
            if (rig == null || finished || !pendingInitialMotion) return;
            pendingInitialMotion = false;
            var sc = CurrentScenario;
            if (sc == Scenario.Spin) rig.SetRigidMotion(Vector3.zero, new Vector3(0f, 10f, 0f));
            else if (sc == Scenario.Flip) rig.SetRigidMotion(Vector3.zero, new Vector3(8f, 0f, 0f));
            initialL = Vector3.zero;
        }

        public void PostPhysics(float dt)
        {
            if (rig == null || finished) return;
            var sc = CurrentScenario;

            if (runTime == 0f)
            {
                initialL = rig.AngularMomentumAboutCom();
                if (rig.Coupling.Settings.gravityCompensation)
                    results.Add($"[S1] gravity forces={rig.Coupling.GravityForceCount}, dof суставов={rig.Coupling.JointDofSum}");
            }
            runTime += dt;

            float grip = Mathf.Max(rig.Coupling.GripDrift(true), rig.Coupling.GripDrift(false));
            float foot = Mathf.Max(rig.Coupling.FootOffset(true), rig.Coupling.FootOffset(false));
            float speed = rig.MaxBodySpeed();
            m.MaxGrip = Mathf.Max(m.MaxGrip, grip);
            m.SumGrip += grip;
            m.Samples++;
            if (runTime >= SettleTime(sc)) m.MaxGripSettled = Mathf.Max(m.MaxGripSettled, grip);
            m.MaxFoot = Mathf.Max(m.MaxFoot, foot);
            m.MaxSpeed = Mathf.Max(m.MaxSpeed, speed);
            if (speed > 60f || float.IsNaN(speed)) m.Exploded = true;

            if (sc == Scenario.Spin || sc == Scenario.Flip)
            {
                if (logMomentum && Mathf.RoundToInt(runTime / dt) % 30 == 1)
                {
                    var (ls, lr) = rig.AngularMomentumSplit();
                    Debug.Log($"[S1 L] {CurrentConfig.name} {sc} t={runTime:F2} L_самокат={ls:F2} L_райдер={lr:F2} ω_таз={rig.Rider.Pelvis.angularVelocity:F2} ω_дека={rig.Scooter.Deck.angularVelocity:F2}");
                }
                float l0 = initialL.magnitude;
                if (l0 > 1e-3f) m.MaxDeltaL = Mathf.Max(m.MaxDeltaL, (rig.AngularMomentumAboutCom() - initialL).magnitude / l0);
            }
            float pelvisHeight = rig.Rider.Pelvis.transform.position.y - rig.Scooter.Deck.position.y;
            m.PelvisSag = Mathf.Max(m.PelvisSag, initialPelvisHeight - pelvisHeight);

            if (runTime >= Duration(sc) || m.Exploded)
            {
                Report();
                NextRun();
            }
        }

        void Report()
        {
            var cfg = CurrentConfig;
            var sc = CurrentScenario;
            string line = string.Format(
                "[S1] {0,-18} {1,-5} хват max={2,6:F1}мм ср={3,5:F1}мм после_осадки={4,6:F1}мм | стопа={5,6:F1}мм | таз_осел={6,5:F0}мм | ΔL={7,5:P1} | vmax={8,5:F1} {9}",
                cfg.name, sc, m.MaxGrip * 1000f, m.Samples > 0 ? m.SumGrip / m.Samples * 1000f : 0f,
                m.MaxGripSettled * 1000f, m.MaxFoot * 1000f, m.PelvisSag * 1000f,
                m.MaxDeltaL, m.MaxSpeed, m.Exploded ? "ВЗРЫВ" : "");
            results.Add(line);
            Debug.Log(line);
        }

        void Finish()
        {
            finished = true;
            loop.Speed = 1f;
            loop.MaxTicksPerFrame = 8;
            var sb = new StringBuilder("[S1] ИТОГ\n");
            foreach (var r in results) sb.AppendLine(r);
            Debug.Log(sb.ToString());

            // Для осмотра глазами оставляем первую конфигурацию в покое.
            runIndex = -1;
            loop.SetPhysicsHz(configs[0].hz);
            rig = SpikeRig.Build("S1 inspect", new Pose(Vector3.zero, Quaternion.identity), loop, interpolator,
                scooter, wheels, rider, coupling, scooterMaterial, riderMaterial);
            if (followCamera != null) followCamera.Target = rig.Focus;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 1100, 600));
            GUILayout.Label(finished ? "S1: матрица завершена, на сцене поза для осмотра"
                : $"S1: прогон {runIndex + 1}/{RunCount}  {CurrentConfig.name} / {CurrentScenario}  t={runTime:F2}");
            foreach (var r in results) GUILayout.Label(r);
            GUILayout.EndArea();
        }
    }
}
