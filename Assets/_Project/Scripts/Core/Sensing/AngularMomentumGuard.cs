using UnityEngine;

namespace ScootFoma.Sensing
{
    /// Страж момента импульса в воздухе. Движок теряет и добавляет момент численно (приводы суставов,
    /// отсутствие гироскопического члена у Rigidbody). Страж считает, какую добавку угловой скорости
    /// всей системы как твёрдого тела вокруг общего центра масс нужно сделать, чтобы вернуть L к опорному.
    /// Это исправление погрешности, а не ассист: игроку момента он не добавляет.
    public static class AngularMomentumGuard
    {
        /// Тензор инерции системы относительно точки (3×3 в левом верхнем углу Matrix4x4).
        public static Matrix4x4 SystemInertia(BodyMassState[] bodies, int count, Vector3 about)
        {
            var sum = new Matrix4x4();
            for (int i = 0; i < count; i++)
            {
                var b = bodies[i];
                Matrix4x4 r = Matrix4x4.Rotate(b.InertiaRotation);
                Matrix4x4 local = Matrix4x4.Scale(b.InertiaDiagonal);
                Matrix4x4 own = r * local * r.transpose;
                Vector3 d = b.CenterOfMass - about;
                float dd = Vector3.Dot(d, d);
                for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    float parallelAxis = b.Mass * ((row == col ? dd : 0f) - d[row] * d[col]);
                    sum[row, col] += own[row, col] + parallelAxis;
                }
            }
            sum[3, 3] = 1f;
            return sum;
        }

        /// Добавка угловой скорости всей системы, возвращающая момент импульса к targetMomentum.
        /// maxFraction ограничивает поправку за шаг долей от |target|, чтобы страж не дёргал систему
        /// при больших скачках (например, при касании земли, когда он уже должен быть выключен).
        public static Vector3 Correction(BodyMassState[] bodies, int count, Vector3 targetMomentum,
            float maxFraction, out Vector3 centerOfMass)
        {
            centerOfMass = MomentumMath.CenterOfMass(bodies, count, out float mass);
            if (mass <= 0f) return Vector3.zero;

            Vector3 current = MomentumMath.TotalAngularMomentum(bodies, count, centerOfMass);
            Vector3 error = targetMomentum - current;
            float limit = targetMomentum.magnitude * Mathf.Max(maxFraction, 0f);
            if (limit > 0f) error = Vector3.ClampMagnitude(error, limit);

            Matrix4x4 inertia = SystemInertia(bodies, count, centerOfMass);
            if (Mathf.Abs(inertia.determinant) < 1e-9f) return Vector3.zero;
            return inertia.inverse.MultiplyVector(error);
        }
    }
}
