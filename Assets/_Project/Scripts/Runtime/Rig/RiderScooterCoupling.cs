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
        [Header("Хват рук: кисть-Rigidbody, жёстко на предплечье и шарниром на руле")]
        [Tooltip("Масса кисти, кг. Joint требует Rigidbody, а райдер и самокат — articulation, поэтому кисть — посредник.")]
        public float handMass = 0.4f;
        [Tooltip("massScale кисти в обоих Joint. Больше 1 — кисть «тяжелее» для решателя.")]
        public float gripMassScale = 1f;
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

    /// Связи райдера с самокатом: жёсткие хваты рук и мягкие хваты стоп (пружина с пределом силы),
    /// плюс компенсация гравитации в приводах райдера.
    /// Райдер и самокат — две articulation, а Joint вешается только на Rigidbody. Поэтому кисть — отдельное
    /// лёгкое Rigidbody: Joint «запястье» держит её жёстко на предплечье, Joint «хват» — на грипсе руля
    /// (линейно жёстко, углы свободны). Отпустить руль = освободить Joint «хват».
    public sealed class RiderScooterCoupling : MonoBehaviour, ISimulationStep
    {
        public sealed class Hand
        {
            public Rigidbody Body;
            public ConfigurableJoint Wrist; // кисть ↔ предплечье
            public ConfigurableJoint Grip;  // кисть ↔ руль
        }

        public CouplingSettings Settings;
        public RiderParts Rider { get; private set; }
        public ScooterParts Scooter { get; private set; }
        public Hand HandL { get; private set; }
        public Hand HandR { get; private set; }
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

            HandL = CreateHand("HandL", rider.ForearmL, rider.HandLocalL, scooter.GripLocalL);
            HandR = CreateHand("HandR", rider.ForearmR, rider.HandLocalR, scooter.GripLocalR);
            if (!settings.grips)
            {
                SetGripHeld(HandL, false);
                SetGripHeld(HandR, false);
            }

            footAnchorDeckL = scooter.Deck.transform.InverseTransformPoint(rider.FootL.transform.TransformPoint(rider.SoleLocalL));
            footAnchorDeckR = scooter.Deck.transform.InverseTransformPoint(rider.FootR.transform.TransformPoint(rider.SoleLocalR));

            int sum = 0;
            foreach (var ab in rider.All) sum += ab.dofCount;
            JointDofSum = sum;

            loop.Register(this);
        }

        Hand CreateHand(string name, ArticulationBody forearm, Vector3 handLocal, Vector3 gripLocal)
        {
            // Кисть — вне иерархий articulation, иначе Rigidbody окажется внутри дерева звеньев.
            var go = new GameObject(name) { layer = forearm.gameObject.layer };
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(forearm.transform.TransformPoint(handLocal), forearm.transform.rotation);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Settings.handMass;
            // Коллайдера нет — инерцию задаём явно (шар ~4 см).
            rb.automaticInertiaTensor = false;
            rb.inertiaTensor = Vector3.one * (0.4f * Settings.handMass * 0.04f * 0.04f);
            rb.automaticCenterOfMass = false;
            rb.centerOfMass = Vector3.zero;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.interpolation = RigidbodyInterpolation.None;

            var hand = new Hand { Body = rb };
            hand.Wrist = Joint(go, forearm, handLocal, angularLocked: true);
            hand.Grip = Joint(go, Scooter.Bar, gripLocal, angularLocked: false);
            return hand;
        }

        ConfigurableJoint Joint(GameObject handGo, ArticulationBody connected, Vector3 connectedAnchor, bool angularLocked)
        {
            var j = handGo.AddComponent<ConfigurableJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedArticulationBody = connected;
            j.connectedAnchor = connectedAnchor;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            var angular = angularLocked ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Free;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = angular;
            j.enableCollision = false;
            j.massScale = Settings.gripMassScale;
            return j;
        }

        /// Держит / отпускает грипсу (линейные оси Joint «хват»).
        public static void SetGripHeld(Hand hand, bool held)
        {
            var motion = held ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Free;
            hand.Grip.xMotion = hand.Grip.yMotion = hand.Grip.zMotion = motion;
        }

        /// Наибольшее расхождение в цепочке предплечье – кисть – грипса, м.
        public float GripDrift(bool left)
        {
            var hand = left ? HandL : HandR;
            var forearm = left ? Rider.ForearmL : Rider.ForearmR;
            var handLocal = left ? Rider.HandLocalL : Rider.HandLocalR;
            var gripLocal = left ? Scooter.GripLocalL : Scooter.GripLocalR;
            Vector3 h = hand.Body.position;
            float wrist = Vector3.Distance(h, forearm.transform.TransformPoint(handLocal));
            float grip = Vector3.Distance(h, Scooter.Bar.transform.TransformPoint(gripLocal));
            return Mathf.Max(wrist, grip);
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
