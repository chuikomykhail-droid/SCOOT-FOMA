using ScootFoma.Input;
using UnityEngine;

namespace ScootFoma.Intent
{
    public enum RightStickOwner { Torso = 0, RearFoot = 1, Feet = 2 }
    public enum LeftStickOwner { Hands = 0, BarSpin = 1 }

    /// Намерения игрока на тик: целевые позы эффекторов (единичный круг/отрезок) и дискретные действия.
    /// Значения не зависят от фазы (земля/воздух/слайд): фаза меняет ответ физики, а не смысл органа управления.
    public struct IntentFrame
    {
        // Стики → эффекторы. Оси: x — вправо, y — вверх стика (от себя / вперёд).
        public Vector2 Torso;     // RS: корпус (наклон, скрутка, мах вперёд/назад)
        public Vector2 Hands;     // LS: руки и руль (руление; к себе/от себя)
        public Vector2 RearFoot;  // RS при RT: задняя нога (пинок деки, тормоз о крыло)
        public Vector2 Feet;      // RS при RB: обе ноги свободны (резерв)
        public float BarSpin;     // LS X при LB: раскрутка руля

        public RightStickOwner RightOwner;
        public LeftStickOwner LeftOwner;

        // Курки
        public float Crouch;      // LT: сгиб ног 0..1
        public float CrouchRate;  // 1/с, отрицательная при отпускании — сила отрыва

        // Хваты
        public bool HandsLoosened;    // LB
        public bool FeetFree;         // RB
        public bool ScooterReleased;  // LB+RB: самокат летит отдельно

        // Кнопки
        public bool PushHeld;
        public bool PushPressed;      // A, нажатие в этот тик
        public bool StanceSwitch;     // L3
        public bool ResetRequested;   // R3
        public Vector2 Camera;        // D-pad, нажатие в этот тик

        // Динамика стиков
        public Vector2 RightStickVelocity;
        public Vector2 LeftStickVelocity;
        public Flick RightFlick;      // эффектор — RightOwner на этом тике
        public Flick LeftFlick;       // эффектор — LeftOwner на этом тике

        public bool RearFootActive => RightOwner == RightStickOwner.RearFoot;
    }
}
