using ScootFoma.Data;
using UnityEngine;

namespace ScootFoma.Input
{
    public struct ProcessedInput
    {
        public double Time;
        public StickState Left;
        public StickState Right;
        public float LeftTrigger;
        public float RightTrigger;
        /// Скорость курков, 1/с. Отрицательная — отпускание (у LT это сила отрыва).
        public float LeftTriggerRate;
        public float RightTriggerRate;

        public bool LeftBumper, RightBumper, Push;
        public bool PushPressed, LeftStickClicked, RightStickClicked;
        public Vector2 DPad;
        public Vector2 DPadPressed; // ненулевая ось — нажата в этот тик
    }

    /// Слой ввода: сырые кадры по тикам → отфильтрованный ввод. Без состояния игры, без Unity-объектов.
    public sealed class InputFilter
    {
        readonly StickChannel left = new StickChannel();
        readonly StickChannel right = new StickChannel();
        InputFrame previous;
        float previousLeftTrigger, previousRightTrigger;
        bool hasPrevious;
        double time;

        public void Reset()
        {
            left.Reset();
            right.Reset();
            hasPrevious = false;
            time = 0;
        }

        public ProcessedInput Process(in InputFrame frame, float dt, InputTuning tuning)
        {
            time += dt;
            var p = new ProcessedInput
            {
                Time = time,
                Left = left.Update(frame.LeftStick, frame.LeftStickPress, time, tuning.leftStick, tuning),
                Right = right.Update(frame.RightStick, frame.RightStickPress, time, tuning.rightStick, tuning),
                LeftTrigger = StickMath.ApplyDeadzone(frame.LeftTrigger, tuning.leftTrigger),
                RightTrigger = StickMath.ApplyDeadzone(frame.RightTrigger, tuning.rightTrigger),
                LeftBumper = frame.LeftBumper,
                RightBumper = frame.RightBumper,
                Push = frame.Push,
                DPad = frame.DPad,
            };

            if (hasPrevious && dt > 0f)
            {
                p.LeftTriggerRate = (p.LeftTrigger - previousLeftTrigger) / dt;
                p.RightTriggerRate = (p.RightTrigger - previousRightTrigger) / dt;
                p.PushPressed = frame.Push && !previous.Push;
                p.LeftStickClicked = frame.LeftStickPress && !previous.LeftStickPress;
                p.RightStickClicked = frame.RightStickPress && !previous.RightStickPress;
                p.DPadPressed = new Vector2(
                    Pressed(frame.DPad.x, previous.DPad.x),
                    Pressed(frame.DPad.y, previous.DPad.y));
            }

            previous = frame;
            previousLeftTrigger = p.LeftTrigger;
            previousRightTrigger = p.RightTrigger;
            hasPrevious = true;
            return p;
        }

        static float Pressed(float now, float before) =>
            Mathf.Abs(now) > 0.5f && Mathf.Abs(before) <= 0.5f ? Mathf.Sign(now) : 0f;
    }
}
