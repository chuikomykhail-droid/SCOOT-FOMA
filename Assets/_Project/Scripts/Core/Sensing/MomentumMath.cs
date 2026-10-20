using UnityEngine;

namespace ScootFoma.Sensing
{
    /// Состояние одного твёрдого тела в мировых координатах, достаточное для расчёта импульсов.
    public struct BodyMassState
    {
        public float Mass;
        public Vector3 CenterOfMass;
        public Vector3 Velocity;           // скорость центра масс
        public Vector3 AngularVelocity;
        public Vector3 InertiaDiagonal;    // главные моменты инерции
        public Quaternion InertiaRotation; // мировой поворот главных осей
    }

    public static class MomentumMath
    {
        /// Момент импульса тела относительно точки: собственное вращение + орбитальная часть.
        public static Vector3 AngularMomentum(in BodyMassState body, Vector3 aboutPoint)
        {
            Vector3 omegaLocal = Quaternion.Inverse(body.InertiaRotation) * body.AngularVelocity;
            Vector3 spin = body.InertiaRotation * Vector3.Scale(body.InertiaDiagonal, omegaLocal);
            Vector3 orbital = body.Mass * Vector3.Cross(body.CenterOfMass - aboutPoint, body.Velocity);
            return spin + orbital;
        }

        public static Vector3 CenterOfMass(BodyMassState[] bodies, int count, out float totalMass)
        {
            Vector3 weighted = Vector3.zero;
            totalMass = 0f;
            for (int i = 0; i < count; i++)
            {
                weighted += bodies[i].CenterOfMass * bodies[i].Mass;
                totalMass += bodies[i].Mass;
            }
            return totalMass > 0f ? weighted / totalMass : Vector3.zero;
        }

        public static Vector3 TotalAngularMomentum(BodyMassState[] bodies, int count, Vector3 aboutPoint)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < count; i++)
                sum += AngularMomentum(bodies[i], aboutPoint);
            return sum;
        }
    }
}
