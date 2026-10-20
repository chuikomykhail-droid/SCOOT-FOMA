using UnityEngine;

namespace ScootFoma.Control
{
    /// Аналитический IK для цепочки из двух костей (плечо–предплечье, бедро–голень).
    public static class TwoBoneIK
    {
        /// Возвращает положение среднего сустава (локоть, колено).
        /// Если цель вне досягаемости, цепочка вытягивается в её сторону, и reachable = false.
        public static Vector3 SolveMidJoint(Vector3 root, Vector3 target, float upperLength, float lowerLength,
            Vector3 bendHint, out bool reachable)
        {
            Vector3 toTarget = target - root;
            float distance = toTarget.magnitude;
            Vector3 dir = distance > 1e-6f ? toTarget / distance : Vector3.down;

            float minReach = Mathf.Abs(upperLength - lowerLength) + 1e-4f;
            float maxReach = upperLength + lowerLength - 1e-4f;
            reachable = distance >= minReach && distance <= maxReach;
            float d = Mathf.Clamp(distance, minReach, maxReach);

            // Проекция среднего сустава на ось root→target и высота над ней.
            float along = (upperLength * upperLength - lowerLength * lowerLength + d * d) / (2f * d);
            float height = Mathf.Sqrt(Mathf.Max(upperLength * upperLength - along * along, 0f));

            Vector3 perp = bendHint - Vector3.Dot(bendHint, dir) * dir;
            if (perp.sqrMagnitude < 1e-8f)
                perp = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.forward);
            perp.Normalize();

            return root + dir * along + perp * height;
        }
    }
}
