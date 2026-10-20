using System;
using UnityEngine;

namespace ScootFoma.Wheel
{
    [Serializable]
    public class WheelFrictionTuning
    {
        [Tooltip("Коэффициент бокового сцепления (μ). Ограничивает боковую силу μ·N.")]
        public float lateralGrip = 1.1f;

        [Tooltip("Доля боковой скорости, гасимая за один шаг (0..1). 1 — жёсткое сцепление, больше 1 раскачивает.")]
        [Range(0f, 1f)] public float lateralResponse = 0.8f;

        [Tooltip("Сопротивление качению, доля N.")]
        public float rollingResistance = 0.015f;

        [Tooltip("Скорость, ниже которой сопротивление качению плавно затухает (м/с), чтобы не дрожать на месте.")]
        public float rollingResistanceFadeSpeed = 0.1f;
    }

    public struct WheelContactInput
    {
        public Vector3 Normal;          // нормаль поверхности
        public Vector3 Axle;            // ось вращения колеса (мир)
        public Vector3 ContactVelocity; // скорость точки контакта на колесе
        public float NormalForce;       // Н, из импульсов контакта прошлого шага
        public float Dt;
    }

    /// Анизотропное трение колеса: вдоль качения свободно (+ сопротивление качению),
    /// поперёк — жёсткое сцепление в пределах μN. Нормальную реакцию решает PhysX.
    public static class WheelFriction
    {
        const float Gravity = 9.81f;

        public static Vector3 Force(in WheelContactInput input, WheelFrictionTuning tuning)
        {
            if (input.NormalForce <= 0f || input.Dt <= 0f)
                return Vector3.zero;

            Vector3 rolling = Vector3.Cross(input.Axle, input.Normal);
            if (rolling.sqrMagnitude < 1e-8f)
                return Vector3.zero; // колесо лежит на боку
            rolling.Normalize();
            Vector3 lateral = Vector3.Cross(input.Normal, rolling);

            // Масса, которую несёт колесо, оценивается по нагрузке: так сила сцепления
            // соизмерима со всей системой (самокат + райдер), а не только с декой.
            float loadMass = input.NormalForce / Gravity;
            float vLateral = Vector3.Dot(input.ContactVelocity, lateral);
            float lateralForce = -vLateral * loadMass * tuning.lateralResponse / input.Dt;
            float maxLateral = tuning.lateralGrip * input.NormalForce;
            lateralForce = Mathf.Clamp(lateralForce, -maxLateral, maxLateral);

            float vRolling = Vector3.Dot(input.ContactVelocity, rolling);
            float fade = tuning.rollingResistanceFadeSpeed > 0f
                ? Mathf.Clamp01(Mathf.Abs(vRolling) / tuning.rollingResistanceFadeSpeed)
                : 1f;
            float rollingForce = -Mathf.Sign(vRolling) * fade * tuning.rollingResistance * input.NormalForce;

            return lateral * lateralForce + rolling * rollingForce;
        }
    }
}
