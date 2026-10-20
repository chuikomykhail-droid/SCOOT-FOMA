using System.Collections.Generic;
using ScootFoma.Simulation;
using Unity.Collections;
using UnityEngine;

namespace ScootFoma.Rig
{
    /// Колесо без отдельного тела: сферический коллайдер на деке/руле, контакт с которым правится
    /// через PhysX contact modification. Целевая скорость контакта вдоль качения равна текущей
    /// относительной скорости, поэтому продольного трения нет (колесо катится), а поперёк цель 0 —
    /// полное сцепление в пределах μN. Нормаль и трение решает PhysX с учётом всех масс системы.
    public sealed class RollingContactWheels : MonoBehaviour, ISimulationStep
    {
        sealed class Wheel
        {
            public SphereCollider Collider;
            public Rigidbody Body;
            // Состояние тела на начало шага: колбэк идёт на потоке физики, Unity API там трогать нельзя.
            public Vector3 Com, Velocity, AngularVelocity, Axle;
            public float RollingSpeedLoss; // м/с за шаг: μ_rr·g·dt
        }

        /// Знак соглашения PhysX для targetVelocity (скорость body0 относительно body1).
        public static float TargetVelocitySign = 1f;

        /// Диагностика: сколько точек контакта колёс изменено с начала сессии.
        public static int ModifiedContactCount;
        public static int CallbackCount;

        static readonly Dictionary<EntityId, Wheel> Registry = new Dictionary<EntityId, Wheel>();
        static bool subscribed;

        readonly List<Wheel> wheels = new List<Wheel>();
        SimulationLoop loop;

        [Tooltip("Коэффициент сопротивления качению μ_rr: продольная цель контакта отстаёт на μ_rr·g·dt за шаг.")]
        public float rollingResistance = 0.015f;

        public int Order => SimulationOrder.Actuation;

        public void Init(SimulationLoop simulationLoop, params (SphereCollider collider, Rigidbody body)[] wheelColliders)
        {
            loop = simulationLoop;
            foreach (var (collider, body) in wheelColliders)
            {
                collider.hasModifiableContacts = true;
                var w = new Wheel { Collider = collider, Body = body };
                wheels.Add(w);
                Registry[collider.GetEntityId()] = w;
            }
            Capture(loop != null ? loop.Dt : Time.fixedDeltaTime);
            Subscribe();
            if (loop != null) loop.Register(this);
        }

        public void PrePhysics(float dt) => Capture(dt);
        public void PostPhysics(float dt) { }

        void Capture(float dt)
        {
            foreach (var w in wheels)
            {
                if (w.Body == null) continue;
                w.Com = w.Body.worldCenterOfMass;
                w.Velocity = w.Body.linearVelocity;
                w.AngularVelocity = w.Body.angularVelocity;
                w.Axle = w.Collider.transform.right;
                w.RollingSpeedLoss = rollingResistance * 9.81f * dt;
            }
        }

        void OnDisable()
        {
            if (loop != null) loop.Unregister(this);
            foreach (var w in wheels)
                if (w.Collider != null) Registry.Remove(w.Collider.GetEntityId());
            wheels.Clear();
            if (Registry.Count == 0) Unsubscribe();
        }

        static void Subscribe()
        {
            if (subscribed) return;
            Physics.ContactModifyEvent += OnContactModify;
            subscribed = true;
        }

        static void Unsubscribe()
        {
            if (!subscribed) return;
            Physics.ContactModifyEvent -= OnContactModify;
            subscribed = false;
        }

        static void OnContactModify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            System.Threading.Interlocked.Increment(ref CallbackCount);
            for (int k = 0; k < pairs.Length; k++)
            {
                var pair = pairs[k];
                bool swapped = false;
                if (!Registry.TryGetValue(pair.colliderEntityId, out var w))
                {
                    if (!Registry.TryGetValue(pair.otherColliderEntityId, out w)) continue;
                    swapped = true;
                }

                Vector3 otherVel = swapped ? pair.bodyVelocity : pair.otherBodyVelocity;
                Vector3 otherAng = swapped ? pair.bodyAngularVelocity : pair.otherBodyAngularVelocity;
                Vector3 otherPos = swapped ? pair.position : pair.otherPosition;

                for (int i = 0; i < pair.contactCount; i++)
                {
                    Vector3 p = pair.GetPoint(i);
                    Vector3 n = pair.GetNormal(i);
                    Vector3 vWheel = w.Velocity + Vector3.Cross(w.AngularVelocity, p - w.Com);
                    Vector3 vOther = otherVel + Vector3.Cross(otherAng, p - otherPos);
                    Vector3 rel = vWheel - vOther;

                    Vector3 rolling = Vector3.Cross(w.Axle, n);
                    if (rolling.sqrMagnitude < 1e-8f) continue; // колесо на боку: обычное трение
                    rolling.Normalize();
                    float vRoll = Vector3.Dot(rel, rolling);
                    vRoll -= Mathf.Sign(vRoll) * Mathf.Min(Mathf.Abs(vRoll), w.RollingSpeedLoss);

                    // body0 − body1: если колесо второе, относительная скорость с обратным знаком.
                    Vector3 target = rolling * (vRoll * (swapped ? -1f : 1f) * TargetVelocitySign);
                    pair.SetTargetVelocity(i, target);
                    System.Threading.Interlocked.Increment(ref ModifiedContactCount);
                }
            }
        }
    }
}
