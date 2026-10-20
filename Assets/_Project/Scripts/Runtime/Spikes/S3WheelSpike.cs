using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScootFoma.Presentation;
using ScootFoma.Rig;
using ScootFoma.Simulation;
using UnityEngine;

namespace ScootFoma.Spikes
{
    /// Спайк S3: модель колеса. Сравнивает колёса-тела PhysX и колёса-коллайдеры с собственным анизотропным трением.
    /// Попутно S2: дрожание визуала с интерполяцией и без. Райдер заменён грузом 75 кг на деке.
    [DefaultExecutionOrder(950)]
    public sealed class S3WheelSpike : MonoBehaviour, ISimulationStep
    {
        public enum Test { Straight, Turn }

        [Serializable]
        public class Run
        {
            public string name;
            public Test test;
            public bool interpolate = true;
        }

        [SerializeField] SimulationLoop loop;
        [SerializeField] PoseInterpolator interpolator;
        [SerializeField] FollowCamera followCamera;
        [SerializeField] Material scooterMaterial;
        [SerializeField] Material groundMaterial;

        // CustomFriction отклонён по итогам спайка (раскачка от оценки нормальной силы по импульсам),
        // его можно вернуть в список для сравнения.
        // Самокат-articulation с вращающимися колёсами. Поворот и прямая; три одинаковых прямых —
        // отпечатки состояния должны совпасть побитово (реплеи ввода как регрессионные тесты).
        [SerializeField] List<Run> runs = new List<Run>
        {
            new Run { name = "warmup", test = Test.Straight }, // не участвует в сравнении: первый прогон сессии
            new Run { name = "turn", test = Test.Turn },
            new Run { name = "straight #1", test = Test.Straight },
            new Run { name = "straight #2", test = Test.Straight },
            new Run { name = "straight #3", test = Test.Straight },
        };
        [Tooltip("Пол из отдельных плиток-коллайдеров вместо одного меша: демонстрирует «призрачные» удары на швах.")]
        [SerializeField] bool tiledGround;

        public ScooterBuildSettings scooter = new ScooterBuildSettings();
        [Header("Груз вместо райдера")]
        public float loadMass = 75f;
        public Vector3 loadCenterOfMass = new Vector3(0f, 0.8f, 0f);
        public Vector3 loadInertia = new Vector3(12f, 2.5f, 12f);
        [Tooltip("Тестовая стабилизация крена: без райдера самокат с грузом падает набок.")]
        public float rollAssistStiffness = 3000f;
        public float rollAssistDamping = 300f;
        [Tooltip("Стабилизатор выключается, если крен больше этого: иначе он раскручивает и подбрасывает упавший самокат.")]
        public float rollAssistMaxAngle = 30f;
        public float rollAssistMaxTorque = 800f;
        [Header("Тесты")]
        public float straightSpeed = 5f;
        public float turnSpeed = 4f;
        public float turnSteerDegrees = 8f;
        public float bumpZ = 8f;
        public float bumpHeight = 0.02f;
        public float duration = 4f;

        struct Metrics
        {
            public Vector3 StartPos;
            public float StartSpeed, MaxAbsX, VertSqSum, MaxVertAfterBump, YawRateSum, SpeedSum, LateralSqSum, SteerSum;
            public int VertSamples, TurnSamples, Ticks;
            // B: состояние деки с полной точностью в фиксированные тики
            public string FingerprintBeforeBump, FingerprintEnd;
            // S2
            public float FrameSpeedSum, FrameSpeedSqSum;
            public int FrameSamples;
            public Vector3 LastVisualPos;
            public bool HasLastVisual;
        }

        const float SettleDuration = 0.5f;

        SpikeRig rig;
        int runIndex = -1;
        float runTime;
        float settleTime;
        bool pendingInitialMotion;
        Metrics m;
        readonly List<string> results = new List<string>();
        bool finished;

        public int Order => SimulationOrder.Sensing;
        Run Current => runs[runIndex];

        void Start()
        {
            if (loop == null) loop = FindAnyObjectByType<SimulationLoop>();
            if (interpolator == null) interpolator = FindAnyObjectByType<PoseInterpolator>();
            if (followCamera == null) followCamera = FindAnyObjectByType<FollowCamera>();
            BuildGround();
            loop.Register(this);
            NextRun();
        }

        void OnDisable() { if (loop != null) loop.Unregister(this); }

        /// Пол: один меш-коллайдер с сеткой 1×1 м (или, для демонстрации, отдельные плитки-коллайдеры,
        /// на швах которых колёса получают «призрачные» удары). Плюс бугор высотой bumpHeight.
        void BuildGround()
        {
            var root = new GameObject("S3 Ground").transform;
            int groundLayer = LayerMask.NameToLayer("Ground");
            var mat = ScooterRigBuilder.Material("Ground", 0.9f);
            const int minX = -20, maxX = 20, minZ = -10, maxZ = 36; // круг поворота R≈4 м с центром на x≈±4 обязан помещаться
            if (tiledGround)
            {
                for (int x = minX; x < maxX; x++)
                for (int z = minZ; z < maxZ; z++)
                {
                    var tile = new GameObject($"Tile {x},{z}") { layer = groundLayer };
                    tile.transform.SetParent(root, false);
                    tile.transform.position = new Vector3(x + 0.5f, -0.05f, z + 0.5f);
                    var c = tile.AddComponent<BoxCollider>();
                    c.size = new Vector3(1f, 0.1f, 1f);
                    c.sharedMaterial = mat;
                }
            }
            else
            {
                var go = new GameObject("Ground mesh") { layer = groundLayer };
                go.transform.SetParent(root, false);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = GridMesh(minX, maxX, minZ, maxZ);
                mc.sharedMaterial = mat;
            }
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Ground (visual)";
            Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(root, false);
            visual.transform.position = new Vector3(0f, -0.051f, 13f);
            visual.transform.localScale = new Vector3(40f, 0.1f, 46f);
            if (groundMaterial != null) visual.GetComponent<Renderer>().sharedMaterial = groundMaterial;

            // Бугор: лежащая капсула, торчащая из пола на bumpHeight
            const float bumpRadius = 0.1f;
            var bump = new GameObject("Bump") { layer = groundLayer };
            bump.transform.SetParent(root, false);
            bump.transform.position = new Vector3(0f, bumpHeight - bumpRadius, bumpZ);
            var cap = bump.AddComponent<CapsuleCollider>();
            cap.direction = 0;
            cap.radius = bumpRadius;
            cap.height = 2f;
            cap.sharedMaterial = mat;
            var bumpVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(bumpVisual.GetComponent<Collider>());
            bumpVisual.transform.SetParent(bump.transform, false);
            bumpVisual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            bumpVisual.transform.localScale = new Vector3(bumpRadius * 2f, 1f, bumpRadius * 2f);
        }

        static Mesh GridMesh(int minX, int maxX, int minZ, int maxZ)
        {
            int nx = maxX - minX, nz = maxZ - minZ;
            var vertices = new List<Vector3>((nx + 1) * (nz + 1));
            var triangles = new List<int>(nx * nz * 6);
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
                vertices.Add(new Vector3(minX + x, 0f, minZ + z));
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i0 = z * (nx + 1) + x, i1 = i0 + 1, i2 = i0 + nx + 1, i3 = i2 + 1;
                triangles.AddRange(new[] { i0, i2, i1, i1, i2, i3 });
            }
            var mesh = new Mesh { name = "S3 Ground" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void NextRun()
        {
            rig?.Destroy();
            rig = null;
            runIndex++;
            if (runIndex >= runs.Count)
            {
                Finish();
                return;
            }

            var run = Current;
            interpolator.Interpolate = run.interpolate;
            var s = JsonUtility.FromJson<ScooterBuildSettings>(JsonUtility.ToJson(scooter));
            s.stabilizers = false;
            s.steerTarget = run.test == Test.Turn ? turnSteerDegrees : 0f;
            s.steerStiffness = 2000f; // руль держит угол жёстко: проверяем колёса, а не колонку
            s.steerDamping = 50f;

            var pose = new Pose(new Vector3(0f, 0.002f, 0f), Quaternion.identity);
            rig = SpikeRig.Build($"S3 {run.name}", pose, loop, interpolator, s, null, null, scooterMaterial, null);
            var deck = rig.Scooter.Deck;
            deck.mass = loadMass;
            deck.automaticCenterOfMass = false;
            deck.centerOfMass = loadCenterOfMass;
            deck.automaticInertiaTensor = false;
            deck.inertiaTensor = loadInertia;
            deck.inertiaTensorRotation = Quaternion.identity;
            if (followCamera != null) followCamera.Target = rig.Focus;

            runTime = 0f;
            settleTime = 0f;
            m = default;
            pendingInitialMotion = true;
        }

        public void PrePhysics(float dt)
        {
            if (rig == null || finished) return;
            var deck = rig.Scooter.Deck;

            // Сначала самокат садится на колёса, потом получает скорость: иначе стартовый удар портит замер.
            settleTime += dt;
            if (pendingInitialMotion && settleTime >= SettleDuration)
            {
                pendingInitialMotion = false;
                m.StartPos = deck.transform.position;
                float v = Current.test == Test.Turn ? turnSpeed : straightSpeed;
                rig.SetRigidMotion(new Vector3(0f, 0f, v), Vector3.zero);
                float spin = v / scooter.wheelRadius;
                rig.Scooter.SetWheelSpin(spin);
                m.StartSpeed = v;
            }

            // Тестовая стабилизация крена вокруг продольной оси деки
            Vector3 forward = deck.transform.forward;
            Vector3 upTarget = Vector3.ProjectOnPlane(Vector3.up, forward).normalized;
            float rollError = Vector3.SignedAngle(deck.transform.up, upTarget, forward) * Mathf.Deg2Rad;
            float rollRate = Vector3.Dot(deck.angularVelocity, forward);
            // Только пока самокат почти вертикален и с ограниченным моментом: упавший самокат оставляем лежать.
            if (Mathf.Abs(rollError) * Mathf.Rad2Deg <= rollAssistMaxAngle && Vector3.Dot(deck.transform.up, Vector3.up) > 0f)
            {
                float torque = Mathf.Clamp(rollError * rollAssistStiffness - rollRate * rollAssistDamping, -rollAssistMaxTorque, rollAssistMaxTorque);
                deck.AddTorque(forward * torque);
            }
        }

        public void PostPhysics(float dt)
        {
            if (rig == null || finished || pendingInitialMotion) return;
            runTime += dt;
            m.Ticks++;
            var deck = rig.Scooter.Deck;
            Vector3 v = deck.linearVelocity;

            // Отпечатки по номеру тика, а не по времени: так сравнение не зависит от накопления float.
            int beforeBumpTick = Mathf.RoundToInt(1f / dt);
            int endTick = Mathf.RoundToInt(duration / dt);
            if (m.Ticks == beforeBumpTick) m.FingerprintBeforeBump = Fingerprint(deck);
            if (m.Ticks == endTick) m.FingerprintEnd = Fingerprint(deck);

            m.MaxAbsX = Mathf.Max(m.MaxAbsX, Mathf.Abs(deck.transform.position.x - m.StartPos.x));
            bool beforeBump = deck.transform.position.z < bumpZ - 1f;
            if (Current.test == Test.Straight)
            {
                if (beforeBump && runTime > 0.2f)
                {
                    m.VertSqSum += v.y * v.y;
                    m.VertSamples++;
                }
                if (!beforeBump) m.MaxVertAfterBump = Mathf.Max(m.MaxVertAfterBump, Mathf.Abs(v.y));
            }
            else if (runTime > 1f)
            {
                m.YawRateSum += Mathf.Abs(deck.angularVelocity.y);
                m.SteerSum += rig.Scooter.SteerAngle;
                m.SpeedSum += Vector3.ProjectOnPlane(v, Vector3.up).magnitude;
                Vector3 rearPoint = deck.transform.TransformPoint(new Vector3(0f, -0.03f, scooter.wheelbaseRear));
                float lateral = Vector3.Dot(deck.GetPointVelocity(rearPoint), deck.transform.right);
                m.LateralSqSum += lateral * lateral;
                m.TurnSamples++;
            }

            if (m.Ticks >= endTick)
            {
                Report();
                NextRun();
            }
        }

        /// Положение и скорость деки побитово (формат R) — для проверки детерминизма.
        static string Fingerprint(ArticulationBody deck)
        {
            Vector3 p = deck.transform.position, v = deck.linearVelocity;
            Quaternion q = deck.transform.rotation;
            return string.Join(",", new[] { p.x, p.y, p.z, v.x, v.y, v.z, q.x, q.y, q.z, q.w }
                .Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// S2: скорость визуала от кадра к кадру. При ровной езде без интерполяции она скачет.
        void LateUpdate()
        {
            if (rig == null || finished || pendingInitialMotion || rig.Focus == null || Current.test != Test.Straight) return;
            Vector3 p = rig.Focus.position;
            float dt = Time.unscaledDeltaTime;
            if (m.HasLastVisual && dt > 0f && runTime > 0.3f && rig.Scooter.Deck.transform.position.z < bumpZ - 1f)
            {
                float s = (p - m.LastVisualPos).magnitude / dt;
                m.FrameSpeedSum += s;
                m.FrameSpeedSqSum += s * s;
                m.FrameSamples++;
            }
            m.LastVisualPos = p;
            m.HasLastVisual = true;
        }

        void Report()
        {
            var run = Current;
            var deck = rig.Scooter.Deck;
            string line;
            if (run.test == Test.Straight)
            {
                float distance = deck.transform.position.z - m.StartPos.z;
                float speedKeep = deck.linearVelocity.z / m.StartSpeed;
                float vertRms = m.VertSamples > 0 ? Mathf.Sqrt(m.VertSqSum / m.VertSamples) : 0f;
                float mean = m.FrameSamples > 0 ? m.FrameSpeedSum / m.FrameSamples : 0f;
                float variance = m.FrameSamples > 0 ? m.FrameSpeedSqSum / m.FrameSamples - mean * mean : 0f;
                float cv = mean > 1e-3f ? Mathf.Sqrt(Mathf.Max(variance, 0f)) / mean : 0f;
                line = string.Format(
                    "[S3] {0,-26} путь={1,5:F2}м скорость_сохр={2,5:P0} увод_x={3,5:F0}мм | дрожь_по_швам vy_rms={4,5:F3}м/с | бугор vy_max={5,5:F2}м/с | [S2] визуал CV={6,5:P1} кадров={7}",
                    run.name, distance, speedKeep, m.MaxAbsX * 1000f, vertRms, m.MaxVertAfterBump, cv, m.FrameSamples);
            }
            else
            {
                float yaw = m.TurnSamples > 0 ? m.YawRateSum / m.TurnSamples : 0f;
                float speed = m.TurnSamples > 0 ? m.SpeedSum / m.TurnSamples : 0f;
                float radius = yaw > 1e-3f ? speed / yaw : float.PositiveInfinity;
                float wheelbase = scooter.columnZ - scooter.wheelbaseRear;
                float expected = wheelbase / Mathf.Tan(turnSteerDegrees * Mathf.Deg2Rad);
                float lateralRms = m.TurnSamples > 0 ? Mathf.Sqrt(m.LateralSqSum / m.TurnSamples) : 0f;
                float steer = m.TurnSamples > 0 ? m.SteerSum / m.TurnSamples : 0f;
                float expectedFromActualSteer = wheelbase / Mathf.Tan(Mathf.Abs(steer) * Mathf.Deg2Rad);
                line = string.Format(
                    "[S3] {0,-26} радиус={1,5:F2}м ожидаемый={2,5:F2}м (ошибка {3,5:P0}) | руль факт={4,5:F2}° → радиус по факту={5,5:F2}м | боковой снос заднего колеса rms={6,5:F3}м/с",
                    run.name, radius, expected, (radius - expected) / expected, steer, expectedFromActualSteer, lateralRms);
            }
            fingerprints.Add((run.name, m.FingerprintBeforeBump, m.FingerprintEnd));
            results.Add(line);
            Debug.Log(line);
        }

        readonly List<(string name, string beforeBump, string end)> fingerprints = new List<(string, string, string)>();

        /// B: прогоны с одинаковым именем до « #» должны дать побитово одинаковые отпечатки.
        void ReportDeterminism(StringBuilder sb)
        {
            foreach (var group in fingerprints.GroupBy(f => f.name.Split(new[] { " #" }, StringSplitOptions.None)[0]).Where(g => g.Count() > 1))
            {
                var first = group.First();
                bool sameBefore = group.All(f => f.beforeBump == first.beforeBump);
                bool sameEnd = group.All(f => f.end == first.end);
                string line = $"[S3] детерминизм «{group.Key}» ×{group.Count()}: до бугра {(sameBefore ? "СОВПАДАЕТ" : "РАЗНЫЙ")}, конец {(sameEnd ? "СОВПАДАЕТ" : "РАЗНЫЙ")}";
                sb.AppendLine(line);
                if (!sameBefore || !sameEnd)
                    foreach (var f in group) sb.AppendLine($"    {f.name}: до={f.beforeBump} конец={f.end}");
            }
        }

        void Finish()
        {
            finished = true;
            interpolator.Interpolate = true;
            var sb = new StringBuilder($"[S3] ИТОГ (частота кадров ~{1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):F0} fps, физика {loop.PhysicsHz:F0} Гц)\n");
            foreach (var r in results) sb.AppendLine(r);
            ReportDeterminism(sb);            Debug.Log(sb.ToString());
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 1200, 400));
            GUILayout.Label(finished ? "S3: все прогоны завершены" : $"S3: {runIndex + 1}/{runs.Count} {Current.name}  t={runTime:F2}");
            foreach (var r in results) GUILayout.Label(r);
            GUILayout.EndArea();
        }
    }
}
