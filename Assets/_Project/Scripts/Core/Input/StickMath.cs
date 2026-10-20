using UnityEngine;

namespace ScootFoma.Input
{
    public static class StickMath
    {
        /// Радиальная мёртвая зона с перемасштабированием: выход непрерывен и проходит от 0 до 1.
        public static Vector2 ApplyDeadzone(Vector2 raw, StickTuning t)
        {
            float magnitude = raw.magnitude;
            if (magnitude <= t.innerDeadzone || magnitude < 1e-6f) return Vector2.zero;
            float range = Mathf.Max(t.outerDeadzone - t.innerDeadzone, 1e-4f);
            float scaled = Mathf.Clamp01((magnitude - t.innerDeadzone) / range);
            return raw / magnitude * Mathf.Pow(scaled, t.exponent);
        }

        public static float ApplyDeadzone(float raw, TriggerTuning t)
        {
            float range = Mathf.Max(t.outerDeadzone - t.deadzone, 1e-4f);
            return Mathf.Clamp01((raw - t.deadzone) / range);
        }

        public static Vector2 ClampToUnitDisk(Vector2 v) => v.sqrMagnitude > 1f ? v.normalized : v;
    }
}
