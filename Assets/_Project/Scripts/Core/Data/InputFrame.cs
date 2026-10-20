using System;
using UnityEngine;

namespace ScootFoma.Data
{
    /// Сырое состояние геймпада на один тик симуляции. Всё, что ниже по цепочке, считается только из этих кадров,
    /// поэтому запись кадров по тикам воспроизводится побитово.
    [Serializable]
    public struct InputFrame
    {
        public Vector2 LeftStick;
        public Vector2 RightStick;
        public float LeftTrigger;
        public float RightTrigger;
        public bool LeftBumper;
        public bool RightBumper;
        public bool Push;            // A
        public bool LeftStickPress;  // L3
        public bool RightStickPress; // R3
        public Vector2 DPad;
    }
}
