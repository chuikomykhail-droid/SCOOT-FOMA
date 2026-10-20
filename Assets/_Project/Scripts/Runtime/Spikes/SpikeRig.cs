using System.Collections.Generic;
using ScootFoma.Presentation;
using ScootFoma.Rig;
using ScootFoma.Sensing;
using ScootFoma.Simulation;
using ScootFoma.Wheel;
using UnityEngine;

namespace ScootFoma.Spikes
{
    /// Сборка самоката (и, опционально, райдера) для спайков: физика, связи, визуал, сбор метрик.
    public sealed class SpikeRig
    {
        public Transform Root;
        public Transform VisualRoot;
        public ScooterParts Scooter;
        public RiderParts Rider;
        public RiderScooterCoupling Coupling;
        public WheelFrictionDriver DeckWheels, BarWheels;
        public Transform Focus; // визуал деки — цель камеры

        BodyMassState[] massStates;

        public static SpikeRig Build(string name, Pose pose, SimulationLoop loop, PoseInterpolator interpolator,
            ScooterBuildSettings scooter, WheelFrictionTuning wheelTuning,
            RiderBuildSettings rider, CouplingSettings coupling, Material scooterMat, Material riderMat,
            bool gyroscopic = true)
        {
            var rig = new SpikeRig();
            rig.Root = new GameObject(name).transform;
            rig.VisualRoot = new GameObject(name + " (visuals)").transform;

            int scooterLayer = LayerMask.NameToLayer("Scooter");
            int riderLayer = LayerMask.NameToLayer("Rider");
            Physics.IgnoreLayerCollision(riderLayer, riderLayer, true);

            rig.Scooter = ScooterRigBuilder.Build(rig.Root, pose, scooter, scooterLayer);
            if (gyroscopic)
                rig.Root.gameObject.AddComponent<GyroscopicRigidbodies>().Init(loop, rig.Scooter.Bodies);

            if (scooter.wheelModel == WheelModel.CustomFriction)
            {
                rig.DeckWheels = rig.Scooter.Deck.gameObject.AddComponent<WheelFrictionDriver>();
                rig.DeckWheels.Init(loop, wheelTuning, rig.Scooter.RearWheelCollider);
                rig.BarWheels = rig.Scooter.Bar.gameObject.AddComponent<WheelFrictionDriver>();
                rig.BarWheels.Init(loop, wheelTuning, rig.Scooter.FrontWheelCollider);
            }
            else if (scooter.wheelModel == WheelModel.RollingContact)
            {
                rig.Root.gameObject.AddComponent<RollingContactWheels>().Init(loop,
                    (rig.Scooter.FrontWheelCollider, rig.Scooter.Bar),
                    (rig.Scooter.RearWheelCollider, rig.Scooter.Deck));
            }

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

            foreach (var rb in rig.Scooter.Bodies)
            {
                var v = VisualFactory.MirrorBody(rb, rig.VisualRoot, interpolator, scooterMat);
                if (rb == rig.Scooter.Deck) rig.Focus = v;
            }

            int count = rig.Scooter.Bodies.Length + (rig.Rider != null ? rig.Rider.All.Length : 0);
            rig.massStates = new BodyMassState[count];
            return rig;
        }

        /// Удаляет риг сразу из физики (SetActive) и затем уничтожает объекты.
        public void Destroy()
        {
            if (Root != null) { Root.gameObject.SetActive(false); Object.Destroy(Root.gameObject); }
            if (VisualRoot != null) Object.Destroy(VisualRoot.gameObject);
        }

        public IEnumerable<Component> AllBodies()
        {
            foreach (var rb in Scooter.Bodies) yield return rb;
            if (Rider != null) foreach (var ab in Rider.All) yield return ab;
        }

        /// Задаёт всей системе движение твёрдого тела: v0 + ω × (r − центр масс).
        public void SetRigidMotion(Vector3 linear, Vector3 angular)
        {
            Vector3 com = CenterOfMass();
            foreach (var rb in Scooter.Bodies)
            {
                rb.linearVelocity = linear + Vector3.Cross(angular, rb.worldCenterOfMass - com);
                rb.angularVelocity = angular;
            }
            if (Rider != null)
            {
                var root = Rider.Pelvis;
                root.linearVelocity = linear + Vector3.Cross(angular, root.worldCenterOfMass - com);
                root.angularVelocity = angular;
                var zeros = new List<float>();
                int dofs = root.GetJointVelocities(zeros);
                for (int i = 0; i < dofs; i++) zeros[i] = 0f;
                root.SetJointVelocities(zeros);
            }
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

        /// Момент импульса самоката и райдера по отдельности относительно общего центра масс.
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
            foreach (var rb in Scooter.Bodies)
            {
                massStates[i++] = new BodyMassState
                {
                    Mass = rb.mass,
                    CenterOfMass = rb.worldCenterOfMass,
                    Velocity = rb.GetPointVelocity(rb.worldCenterOfMass),
                    AngularVelocity = rb.angularVelocity,
                    InertiaDiagonal = rb.inertiaTensor,
                    InertiaRotation = rb.rotation * rb.inertiaTensorRotation,
                };
            }
            if (Rider == null) return;
            foreach (var ab in Rider.All)
            {
                massStates[i++] = new BodyMassState
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
    }
}
