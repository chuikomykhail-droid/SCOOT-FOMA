using System;
using UnityEngine;

namespace ScootFoma.Input
{
    [Serializable]
    public class StickTuning
    {
        [Tooltip("Радиальная мёртвая зона в центре.")]
        [Range(0f, 0.5f)] public float innerDeadzone = 0.1f;
        [Tooltip("Отклонение, после которого стик считается на упоре.")]
        [Range(0.5f, 1f)] public float outerDeadzone = 0.95f;
        [Tooltip("Кривая отклика: 1 — линейно, >1 — точнее у центра.")]
        [Range(0.5f, 3f)] public float exponent = 1f;
    }

    [Serializable]
    public class TriggerTuning
    {
        [Range(0f, 0.3f)] public float deadzone = 0.05f;
        [Range(0.7f, 1f)] public float outerDeadzone = 0.98f;
    }

    [Serializable]
    public class FlickTuning
    {
        [Tooltip("Порог отклонения «на упоре» для начала и конца маха.")]
        [Range(0.3f, 1f)] public float edge = 0.7f;
        [Tooltip("Мах должен пройти ближе этого радиуса к центру.")]
        [Range(0f, 0.9f)] public float centerRadius = 0.5f;
        [Tooltip("Максимальная длительность маха от упора до упора, с. Замер на Xbox: 48–76 мс.")]
        [Range(0.03f, 0.3f)] public float maxDuration = 0.12f;
    }

    public enum LatchMode
    {
        /// Новый эффектор берёт цель как смещение от положения стика в момент переключения.
        Offset,
        /// Новый эффектор держит прежнюю цель, пока стик не вернётся в центр.
        WaitCenter,
    }

    [Serializable]
    public class LatchTuning
    {
        public LatchMode mode = LatchMode.Offset;
        [Tooltip("Offset: за сколько секунд смещение растворяется и цель снова совпадает со стиком. 0 — никогда.")]
        [Min(0f)] public float recenterTime = 0.3f;
        [Tooltip("WaitCenter: радиус «стик в центре».")]
        [Range(0f, 0.5f)] public float centerRadius = 0.15f;
    }

    /// Все коэффициенты слоя ввода. Живут в ассете, а не в коде.
    [Serializable]
    public class InputTuning
    {
        public StickTuning leftStick = new StickTuning();
        public StickTuning rightStick = new StickTuning();
        public TriggerTuning leftTrigger = new TriggerTuning();
        public TriggerTuning rightTrigger = new TriggerTuning();
        public FlickTuning flick = new FlickTuning();
        public LatchTuning latch = new LatchTuning();
        [Tooltip("Нажатие стика сдвигает ось: столько секунд после нажатия и отпускания L3/R3 ось держит значение до клика.")]
        [Range(0f, 0.2f)] public float clickFreezeTime = 0.06f;
        [Tooltip("Окно оценки скорости стика, с.")]
        [Range(0.004f, 0.05f)] public float velocityWindow = 0.017f;
    }
}
