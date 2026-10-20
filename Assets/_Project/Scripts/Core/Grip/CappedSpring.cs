using UnityEngine;

namespace ScootFoma.Grip
{
    /// Пружина-демпфер с ограничением силы. Это модель мягкого хвата стопы и деки:
    /// прилипание есть, но оторвать ногу можно усилием больше maxForce.
    public static class CappedSpring
    {
        /// offset = цель − текущее положение, relativeVelocity = скорость цели − скорость точки.
        /// Возвращает силу на точку; на опору нужно приложить противоположную.
        public static Vector3 Force(Vector3 offset, Vector3 relativeVelocity, float stiffness, float damping, float maxForce)
        {
            Vector3 force = offset * stiffness + relativeVelocity * damping;
            return Vector3.ClampMagnitude(force, Mathf.Max(maxForce, 0f));
        }
    }
}
