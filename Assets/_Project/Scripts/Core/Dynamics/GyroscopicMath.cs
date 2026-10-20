using UnityEngine;

namespace ScootFoma.Dynamics
{
    /// Гироскопический член ω × (Iω), которого PhysX не считает для Rigidbody.
    /// Без него тело, вращающееся не вокруг главной оси, держит постоянной ω вместо момента импульса.
    /// Неявный шаг (E. Catto, «Numerical Methods», GDC 2015): одна итерация Ньютона для
    /// I(ω₂ − ω₁) + h·ω₂ × Iω₂ = 0. Устойчив при быстром вращении, в отличие от явного момента.
    public static class GyroscopicMath
    {
        /// Изменение угловой скорости за шаг h в системе главных осей тела.
        public static Vector3 DeltaOmegaLocal(Vector3 omegaLocal, Vector3 inertiaDiagonal, float h)
        {
            Vector3 w = omegaLocal;
            Vector3 iw = Vector3.Scale(inertiaDiagonal, w);
            Vector3 f = Vector3.Cross(w, iw) * h;

            // J = I + h·(skew(ω)·I − skew(Iω))
            var j = new Matrix4x4();
            var skewW = Skew(w);
            var skewIw = Skew(iw);
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                float inertia = r == c ? inertiaDiagonal[r] : 0f;
                j[r, c] = inertia + h * (skewW[r, c] * inertiaDiagonal[c] - skewIw[r, c]);
            }
            j[3, 3] = 1f;

            if (Mathf.Abs(j.determinant) < 1e-12f) return Vector3.zero;
            Vector3 delta = j.inverse.MultiplyVector(f);
            return -delta;
        }

        /// Мировой момент, который за шаг h даёт нужное изменение ω.
        public static Vector3 TorqueWorld(Vector3 omegaWorld, Vector3 inertiaDiagonal, Quaternion inertiaRotationWorld, float h)
        {
            if (h <= 0f) return Vector3.zero;
            Vector3 omegaLocal = Quaternion.Inverse(inertiaRotationWorld) * omegaWorld;
            Vector3 dOmega = DeltaOmegaLocal(omegaLocal, inertiaDiagonal, h);
            return inertiaRotationWorld * Vector3.Scale(inertiaDiagonal, dOmega / h);
        }

        static Matrix4x4 Skew(Vector3 v)
        {
            var m = new Matrix4x4();
            m[0, 1] = -v.z; m[0, 2] = v.y;
            m[1, 0] = v.z;  m[1, 2] = -v.x;
            m[2, 0] = -v.y; m[2, 1] = v.x;
            return m;
        }
    }
}
