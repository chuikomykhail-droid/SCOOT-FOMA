using ScootFoma.Dynamics;
using ScootFoma.Simulation;
using UnityEngine;

namespace ScootFoma.Rig
{
    /// Добавляет Rigidbody гироскопический момент (PhysX его не считает, ArticulationBody — считает).
    /// Нужен самокату: без него тейлвип и барспин не по главной оси теряют момент импульса.
    public sealed class GyroscopicRigidbodies : MonoBehaviour, ISimulationStep
    {
        Rigidbody[] bodies = new Rigidbody[0];
        SimulationLoop loop;

        public int Order => SimulationOrder.Actuation;

        public void Init(SimulationLoop simulationLoop, params Rigidbody[] rigidbodies)
        {
            loop = simulationLoop;
            bodies = rigidbodies;
            if (loop != null) loop.Register(this); // без цикла шаги вызывает тест вручную
        }

        public void PrePhysics(float dt)
        {
            foreach (var rb in bodies)
            {
                if (rb == null || rb.isKinematic) continue;
                rb.AddTorque(GyroscopicMath.TorqueWorld(rb.angularVelocity, rb.inertiaTensor,
                    rb.rotation * rb.inertiaTensorRotation, dt));
            }
        }

        public void PostPhysics(float dt) { }

        void OnDisable()
        {
            if (loop != null) loop.Unregister(this);
        }
    }
}
