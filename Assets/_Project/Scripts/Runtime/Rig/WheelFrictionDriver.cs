using ScootFoma.Simulation;
using ScootFoma.Wheel;
using UnityEngine;

namespace ScootFoma.Rig
{
    /// Анизотропное трение колёс-коллайдеров без трения. Нормальную реакцию решает PhysX,
    /// её величину берём из импульсов контактов прошлого шага, боковое сцепление и
    /// сопротивление качению считает WheelFriction. Висит на теле (деке или руле), которому принадлежат колёса.
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WheelFrictionDriver : MonoBehaviour, ISimulationStep
    {
        public WheelFrictionTuning Tuning = new WheelFrictionTuning();

        struct WheelState
        {
            public SphereCollider Collider;
            public float NormalImpulse;
            public Vector3 NormalSum;
            public Vector3 PointSum;
            public int Contacts;
            public float LastNormalForce;
            public Vector3 LastForce;
            public float LastLateralSpeed;
        }

        Rigidbody body;
        SimulationLoop loop;
        WheelState[] wheels = new WheelState[0];

        public int Order => SimulationOrder.Actuation;

        public void Init(SimulationLoop simulationLoop, WheelFrictionTuning tuning, params SphereCollider[] wheelColliders)
        {
            loop = simulationLoop;
            body = GetComponent<Rigidbody>();
            Tuning = tuning;
            wheels = new WheelState[wheelColliders.Length];
            for (int i = 0; i < wheelColliders.Length; i++) wheels[i].Collider = wheelColliders[i];
            if (loop != null) loop.Register(this); // без цикла шаги вызывает тест вручную
        }

        public bool IsGrounded(int index) => wheels[index].LastNormalForce > 0f;
        public float NormalForce(int index) => wheels[index].LastNormalForce;
        public float LateralSpeed(int index) => wheels[index].LastLateralSpeed;

        public void PrePhysics(float dt)
        {
            for (int i = 0; i < wheels.Length; i++)
            {
                ref var w = ref wheels[i];
                if (w.Contacts == 0)
                {
                    w.LastNormalForce = 0f;
                    w.LastForce = Vector3.zero;
                    continue;
                }

                Vector3 normal = w.NormalSum.normalized;
                Vector3 point = w.PointSum / w.Contacts;
                var input = new WheelContactInput
                {
                    Normal = normal,
                    Axle = w.Collider.transform.right,
                    ContactVelocity = body.GetPointVelocity(point),
                    NormalForce = w.NormalImpulse / dt,
                    Dt = dt,
                };
                Vector3 force = WheelFriction.Force(input, Tuning);
                body.AddForceAtPosition(force, point);

                Vector3 lateral = Vector3.Cross(normal, Vector3.Cross(input.Axle, normal).normalized);
                w.LastLateralSpeed = Vector3.Dot(input.ContactVelocity, lateral);
                w.LastNormalForce = input.NormalForce;
                w.LastForce = force;

                w.NormalImpulse = 0f;
                w.NormalSum = Vector3.zero;
                w.PointSum = Vector3.zero;
                w.Contacts = 0;
            }
        }

        public void PostPhysics(float dt) { }

        void OnCollisionEnter(Collision collision) => Accumulate(collision);
        void OnCollisionStay(Collision collision) => Accumulate(collision);

        void Accumulate(Collision collision)
        {
            for (int c = 0; c < collision.contactCount; c++)
            {
                var cp = collision.GetContact(c);
                int index = IndexOf(cp.thisCollider);
                if (index < 0) continue;
                ref var w = ref wheels[index];
                Vector3 n = cp.normal;
                if (Vector3.Dot(n, w.Collider.bounds.center - cp.point) < 0f) n = -n;
                w.NormalImpulse += Mathf.Abs(Vector3.Dot(cp.impulse, n));
                w.NormalSum += n;
                w.PointSum += cp.point;
                w.Contacts++;
            }
        }

        int IndexOf(Collider col)
        {
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i].Collider == col) return i;
            return -1;
        }

        void OnDisable()
        {
            if (loop != null) loop.Unregister(this);
        }
    }
}
