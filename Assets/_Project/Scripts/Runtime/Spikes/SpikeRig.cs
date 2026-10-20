using System.Collections.Generic;
using ScootFoma.Presentation;
using ScootFoma.Rig;
using ScootFoma.Sensing;
using ScootFoma.Simulation;
using UnityEngine;

namespace ScootFoma.Spikes
{
    /// Сборка самоката (и, опционально, райдера) для спайков: физика, связи, визуал, сбор метрик.
    /// Самокат и райдер — две articulation; кисти — Rigidbody-посредники хвата.
    public sealed class SpikeRig
    {
        public Transform Root;
        public Transform VisualRoot;
        public ScooterParts Scooter;
        public RiderParts Rider;
        public RiderScooterCoupling Coupling;
        public Transform Focus; // визуал деки — цель камеры

        BodyMassState[] massStates;
        readonly List<float> jointBuffer = new List<float>(64);

        public static SpikeRig Build(string name, Pose pose, SimulationLoop loop, PoseInterpolator interpolator,
            ScooterBuildSettings scooter, RiderBuildSettings rider, CouplingSettings coupling,
            Material scooterMat, Material riderMat)
        {
            var rig = new SpikeRig();
            rig.Root = new GameObject(name).transform;
            rig.VisualRoot = new GameObject(name + " (visuals)").transform;

            int scooterLayer = LayerMask.NameToLayer("Scooter");
            int riderLayer = LayerMask.NameToLayer("Rider");
            Physics.IgnoreLayerCollision(riderLayer, riderLayer, true);

            rig.Scooter = ScooterRigBuilder.Build(rig.Root, pose, scooter, scooterLayer);

            if (rider != null)
            {
                Vector3 ToRig(Vector3 world) => Quaternion.Inverse(pose.rotation) * (world - pose.position);
                Vector3 gripL = ToRig(rig.Scooter.Bar.transform.TransformPoint(rig.Scooter.GripLocalL));
                Vector3 gripR = ToRig(rig.Scooter.Bar.transform.TransformPoint(rig.Scooter.GripLocalR));
                rig.Rider = RiderRigBuilder.Build(rig.Root, pose, rider, riderLayer, rig.Scooter.DeckTopY, gripL, gripR);
                if (!rig.Rider.ArmsReachable || !rig.Rider.LegsReachable)
                    Debug.LogWarning($"[Rig] Поза сборки недостижима: руки={rig.Rider.ArmsReachable}, ноги={rig.Rider.LegsReachable}");

                // Предплечья перекрывают грипсы — не сталкиваем их с самокатом.
                int scooterMask = 1 << scooterLayer;
                rig.Rider.ForearmL.excludeLayers = scooterMask;
                rig.Rider.ForearmR.excludeLayers = scooterMask;

                rig.Coupling = rig.Root.gameObject.AddComponent<RiderScooterCoupling>();
                rig.Coupling.Init(loop, rig.Rider, rig.Scooter, coupling);

                foreach (var ab in rig.Rider.All)
                    VisualFactory.MirrorBody(ab, rig.VisualRoot, interpolator, riderMat);
            }

            foreach (var ab in rig.Scooter.Bodies)
            {
                var v = VisualFactory.MirrorBody(ab, rig.VisualRoot, interpolator, scooterMat);
                if (ab == rig.Scooter.Deck) rig.Focus = v;
            }

            rig.massStates = new BodyMassState[rig.CountBodies()];
            return rig;
        }

        int CountBodies() => Scooter.Bodies.Length + (Rider != null ? Rider.All.Length + 2 : 0);

        /// Удаляет риг сразу из физики (SetActive) и затем уничтожает объекты.
        public void Destroy()
        {
            if (Root != null) { Root.gameObject.SetActive(false); Object.Destroy(Root.gameObject); }
            if (VisualRoot != null) Object.Destroy(VisualRoot.gameObject);
        }

        IEnumerable<Rigidbody> Hands()
        {
            if (Coupling == null) yield break;
            yield return Coupling.HandL.Body;
            yield return Coupling.HandR.Body;
        }

        /// Задаёт всей системе движение твёрдого тела: v0 + ω × (r − центр масс). Скорости суставов обнуляются.
        public void SetRigidMotion(Vector3 linear, Vector3 angular)
        {
            Vector3 com = CenterOfMass();
            SetRootMotion(Scooter.Deck, linear, angular, com, zeroJoints: true);
            if (Rider != null) SetRootMotion(Rider.Pelvis, linear, angular, com, zeroJoints: true);
            foreach (var hand in Hands())
            {
                hand.linearVelocity = linear + Vector3.Cross(angular, hand.worldCenterOfMass - com);
                hand.angularVelocity = angular;
            }
        }

        void SetRootMotion(ArticulationBody root, Vector3 linear, Vector3 angular, Vector3 com, bool zeroJoints)
        {
            root.linearVelocity = linear + Vector3.Cross(angular, root.worldCenterOfMass - com);
            root.angularVelocity = angular;
            if (!zeroJoints) return;
            int dofs = root.GetJointVelocities(jointBuffer);
            for (int i = 0; i < dofs; i++) jointBuffer[i] = 0f;
            root.SetJointVelocities(jointBuffer);
        }

        public Vector3 CenterOfMass()
        {
            Fill();
            return MomentumMath.CenterOfMass(massStates, massStates.Length, out _);
        }

        public Vector3 AngularMomentumAboutCom()
        {
            Fill();
            Vector3 com = MomentumMath.CenterOfMass(massStates, massStates.Length, out _);
            return MomentumMath.TotalAngularMomentum(massStates, massStates.Length, com);
        }

        /// Момент импульса самоката и райдера (с кистями) по отдельности относительно общего центра масс.
        public (Vector3 scooter, Vector3 rider) AngularMomentumSplit()
        {
            Fill();
            Vector3 com = MomentumMath.CenterOfMass(massStates, massStates.Length, out _);
            int n = Scooter.Bodies.Length;
            Vector3 scooter = Vector3.zero, rider = Vector3.zero;
            for (int i = 0; i < massStates.Length; i++)
            {
                Vector3 l = MomentumMath.AngularMomentum(massStates[i], com);
                if (i < n) scooter += l; else rider += l;
            }
            return (scooter, rider);
        }

        /// Страж момента импульса: добавляет всей системе вращение как твёрдого тела, возвращая L к target.
        /// У articulation меняется только корень — звенья получают ту же добавку при неизменных скоростях суставов.
        public Vector3 GuardAngularMomentum(Vector3 target, float maxFraction)
        {
            Fill();
            Vector3 dOmega = AngularMomentumGuard.Correction(massStates, massStates.Length, target, maxFraction, out Vector3 com);
            if (dOmega.sqrMagnitude < 1e-12f) return dOmega;
            AddRootRotation(Scooter.Deck, dOmega, com);
            if (Rider != null) AddRootRotation(Rider.Pelvis, dOmega, com);
            foreach (var hand in Hands())
            {
                hand.angularVelocity += dOmega;
                hand.linearVelocity += Vector3.Cross(dOmega, hand.worldCenterOfMass - com);
            }
            return dOmega;
        }

        static void AddRootRotation(ArticulationBody root, Vector3 dOmega, Vector3 com)
        {
            root.angularVelocity += dOmega;
            root.linearVelocity += Vector3.Cross(dOmega, root.worldCenterOfMass - com);
        }

        public float MaxBodySpeed()
        {
            Fill();
            float max = 0f;
            foreach (var s in massStates) max = Mathf.Max(max, s.Velocity.magnitude);
            return max;
        }

        void Fill()
        {
            int i = 0;
            foreach (var ab in Scooter.Bodies) massStates[i++] = State(ab);
            if (Rider == null) return;
            foreach (var hand in Hands())
            {
                massStates[i++] = new BodyMassState
                {
                    Mass = hand.mass,
                    CenterOfMass = hand.worldCenterOfMass,
                    Velocity = hand.GetPointVelocity(hand.worldCenterOfMass),
                    AngularVelocity = hand.angularVelocity,
                    InertiaDiagonal = hand.inertiaTensor,
                    InertiaRotation = hand.rotation * hand.inertiaTensorRotation,
                };
            }
            foreach (var ab in Rider.All) massStates[i++] = State(ab);
        }

        static BodyMassState State(ArticulationBody ab) => new BodyMassState
        {
            Mass = ab.mass,
            CenterOfMass = ab.worldCenterOfMass,
            Velocity = ab.GetPointVelocity(ab.worldCenterOfMass),
            AngularVelocity = ab.angularVelocity,
            InertiaDiagonal = ab.inertiaTensor,
            InertiaRotation = ab.transform.rotation * ab.inertiaTensorRotation,
        };
    }
}
