using System;
using System.Collections.Generic;
using ScootFoma.Grip;
using ScootFoma.Simulation;
using UnityEngine;

namespace ScootFoma.Rig
{
    public enum GripState { Held, Loosened, Released, Regrab }

    [Serializable]
    public class CouplingSettings
    {
        [Header("Хват рук (Joint на руле → предплечье)")]
        [Tooltip("massScale на стороне руля. Больше 1 — руль «тяжелее» для решателя хвата, меньше дрейф при большом перепаде масс.")]
        public float gripMassScale = 1f;
        public float gripConnectedMassScale = 1f;
        [Tooltip("Только для диагностики: без хватов рук райдер и самокат не связаны.")]
        public bool grips = true;

        [Header("Прилипание стоп (мягкий хват)")]
        public float footStiffness = 4000f;
        public float footDamping = 100f;
        [Tooltip("Предел силы прилипания, Н. 0 — чистый контакт. Это ползунок ассиста.")]
        public float footMaxForce = 300f;

        [Header("Приводы")]
        [Tooltip("Компенсация гравитации через силы суставов (feed-forward). Верна, только когда тело стоит на опоре.")]
        public bool gravityCompensation = true;
    }

    /// Связи райдера с самокатом: жёсткие хваты рук (Joint) и мягкие хваты стоп (пружина с пределом силы),
    /// плюс компенсация гравитации в приводах райдера.
    public sealed class RiderScooterCoupling : MonoBehaviour, ISimulationStep
    {
        public CouplingSettings Settings;
        public RiderParts Rider { get; private set; }
        public ScooterParts Scooter { get; private set; }
        public ConfigurableJoint GripL { get; private set; }
        public ConfigurableJoint GripR { get; private set; }
        public GripState FootStateL = GripState.Held;
        public GripState FootStateR = GripState.Held;

        /// Число степеней свободы из GetJointGravityForces и сумма dofCount звеньев — для диагностики.
        public int GravityForceCount { get; private set; }
        public int JointDofSum { get; private set; }

        SimulationLoop loop;
        Vector3 footAnchorDeckL, footAnchorDeckR;
        readonly List<float> gravityForces = new List<float>(64);

        public int Order => SimulationOrder.Actuation;

        public void Init(SimulationLoop simulationLoop, RiderParts rider, ScooterParts scooter, CouplingSettings settings)
        {
            loop = simulationLoop;
            Rider = rider;
            Scooter = scooter;
            Settings = settings;

            GripL = CreateGrip(scooter.Bar, scooter.GripLocalL, rider.ForearmL, rider.HandLocalL);
            GripR = CreateGrip(scooter.Bar, scooter.GripLocalR, rider.ForearmR, rider.HandLocalR);
            if (!settings.grips)
            {
                // Joint остаётся как точка измерения, но не связывает тела.
                GripL.xMotion = GripL.yMotion = GripL.zMotion = ConfigurableJointMotion.Free;
                GripR.xMotion = GripR.yMotion = GripR.zMotion = ConfigurableJointMotion.Free;
            }

            footAnchorDeckL = scooter.Deck.transform.InverseTransformPoint(rider.FootL.transform.TransformPoint(rider.SoleLocalL));
            footAnchorDeckR = scooter.Deck.transform.InverseTransformPoint(rider.FootR.transform.TransformPoint(rider.SoleLocalR));

            int sum = 0;
            foreach (var ab in rider.All) sum += ab.dofCount;
            JointDofSum = sum;

            loop.Register(this);
        }

        ConfigurableJoint CreateGrip(Rigidbody bar, Vector3 barAnchor, ArticulationBody forearm, Vector3 handLocal)
        {
            var j = bar.gameObject.AddComponent<ConfigurableJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.anchor = barAnchor;
            j.connectedArticulationBody = forearm;
            j.connectedAnchor = handLocal;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            // Угловая свобода играет роль запястья.
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
            j.enableCollision = false;
            j.massScale = Settings.gripMassScale;
            j.connectedMassScale = Settings.gripConnectedMassScale;
            return j;
        }

        public float GripDrift(bool left)
        {
            var j = left ? GripL : GripR;
            var forearm = left ? Rider.ForearmL : Rider.ForearmR;
            var hand = left ? Rider.HandLocalL : Rider.HandLocalR;
            return Vector3.Distance(Scooter.Bar.transform.TransformPoint(j.anchor), forearm.transform.TransformPoint(hand));
        }

        public float FootOffset(bool left)
        {
            var foot = left ? Rider.FootL : Rider.FootR;
            var sole = left ? Rider.SoleLocalL : Rider.SoleLocalR;
            var anchor = left ? footAnchorDeckL : footAnchorDeckR;
            return Vector3.Distance(Scooter.Deck.transform.TransformPoint(anchor), foot.transform.TransformPoint(sole));
        }

        public void PrePhysics(float dt)
        {
            if (Rider == null) return;

            if (FootStateL == GripState.Held) Adhere(Rider.FootL, Rider.SoleLocalL, footAnchorDeckL);
            if (FootStateR == GripState.Held) Adhere(Rider.FootR, Rider.SoleLocalR, footAnchorDeckR);

            if (Settings.gravityCompensation)
            {
                GravityForceCount = Rider.Pelvis.GetJointGravityForces(gravityForces);
                // У свободного корня в начале списка 6 DoF. Компенсировать их нельзя: это внешняя
                // сила, которая делает тело невесомым. Компенсируем только суставы.
                int rootDofs = GravityForceCount - JointDofSum;
                for (int i = 0; i < rootDofs; i++) gravityForces[i] = 0f;
                Rider.Pelvis.SetJointForces(gravityForces);
            }
        }

        void Adhere(ArticulationBody foot, Vector3 soleLocal, Vector3 deckAnchorLocal)
        {
            if (Settings.footMaxForce <= 0f) return;
            var deck = Scooter.Deck;
            Vector3 footPoint = foot.transform.TransformPoint(soleLocal);
            Vector3 deckPoint = deck.transform.TransformPoint(deckAnchorLocal);
            Vector3 relVel = deck.GetPointVelocity(deckPoint) - foot.GetPointVelocity(footPoint);
            Vector3 f = CappedSpring.Force(deckPoint - footPoint, relVel,
                Settings.footStiffness, Settings.footDamping, Settings.footMaxForce);
            foot.AddForceAtPosition(f, footPoint);
            deck.AddForceAtPosition(-f, deckPoint);
        }

        public void PostPhysics(float dt) { }

        void OnDisable()
        {
            if (loop != null) loop.Unregister(this);
        }
    }
}
