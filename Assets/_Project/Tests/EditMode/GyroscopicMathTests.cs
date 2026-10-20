using NUnit.Framework;
using ScootFoma.Dynamics;
using UnityEngine;

namespace ScootFoma.Tests
{
    public class GyroscopicMathTests
    {
        [Test]
        public void SymmetricBody_HasNoGyroscopicTorque()
        {
            Vector3 d = GyroscopicMath.DeltaOmegaLocal(new Vector3(3f, 6f, 1f), Vector3.one, 1f / 120f);
            Assert.Less(d.magnitude, 1e-5f);
        }

        [Test]
        public void SpinAboutPrincipalAxis_IsSteady()
        {
            Vector3 d = GyroscopicMath.DeltaOmegaLocal(new Vector3(0f, 10f, 0f), new Vector3(0.05f, 0.2f, 0.17f), 1f / 120f);
            Assert.Less(d.magnitude, 1e-5f);
        }

        /// Свободное вращение коробки не по главной оси, 2 с при 120 Гц.
        /// С гироскопическим шагом мировой момент импульса сохраняется (неявный метод слегка
        /// диссипативен — допускаем 5%), без него ошибка многократно больше: так ведёт себя голый Rigidbody.
        [Test]
        public void FreeRotation_ConservesWorldAngularMomentum()
        {
            float withGyro = SimulateRelativeError(true);
            float withoutGyro = SimulateRelativeError(false);
            Assert.Less(withGyro, 0.05f);
            Assert.Greater(withoutGyro, withGyro * 5f);
        }

        static float SimulateRelativeError(bool gyroscopic)
        {
            var inertia = new Vector3(0.0483f, 0.2083f, 0.1733f);
            Quaternion rotation = Quaternion.Euler(20f, 40f, 30f);
            Vector3 omega = new Vector3(3f, 6f, 1f);
            const float h = 1f / 120f;

            Vector3 L(Quaternion r, Vector3 w) => r * Vector3.Scale(inertia, Quaternion.Inverse(r) * w);
            Vector3 l0 = L(rotation, omega);

            for (int i = 0; i < 240; i++)
            {
                if (gyroscopic)
                {
                    Vector3 local = Quaternion.Inverse(rotation) * omega;
                    omega += rotation * GyroscopicMath.DeltaOmegaLocal(local, inertia, h);
                }
                float angle = omega.magnitude * h;
                if (angle > 0f) rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, omega.normalized) * rotation;
            }

            return (L(rotation, omega) - l0).magnitude / l0.magnitude;
        }
    }
}
