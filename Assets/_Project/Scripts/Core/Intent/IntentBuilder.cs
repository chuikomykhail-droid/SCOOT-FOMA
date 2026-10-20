using System;
using ScootFoma.Input;
using UnityEngine;

namespace ScootFoma.Intent
{
    [Serializable]
    public class IntentTuning
    {
        [Tooltip("RT считается зажатым выше этого значения…")]
        [Range(0.1f, 0.9f)] public float rightTriggerOn = 0.6f;
        [Tooltip("…и отпущенным ниже этого (гистерезис против дребезга).")]
        [Range(0.05f, 0.8f)] public float rightTriggerOff = 0.4f;
    }

    /// Ввод → намерения. Раскладка (спецификация, «Раскладка геймпада»):
    /// RS — корпус; RT держать — RS ведёт заднюю ногу; RB держать — RS ведёт обе ноги (приоритет над RT).
    /// LS — руки; LB держать — LS X раскручивает руль. LT — сгиб ног. A — толчок. L3 — стойка, R3 — сброс, D-pad — камера.
    public sealed class IntentBuilder
    {
        // RS: корпус постоянный; нога и обе ноги — временные эффекторы
        readonly StickRouter right = new StickRouter(false, true, true);
        // LS: руки постоянные; раскрутка руля — временная
        readonly StickRouter left = new StickRouter(false, true);
        bool rightTriggerHeld;

        public void Reset()
        {
            right.Reset();
            left.Reset();
            rightTriggerHeld = false;
        }

        public IntentFrame Build(in ProcessedInput input, float dt, InputTuning inputTuning, IntentTuning tuning)
        {
            if (rightTriggerHeld) rightTriggerHeld = input.RightTrigger > tuning.rightTriggerOff;
            else rightTriggerHeld = input.RightTrigger >= tuning.rightTriggerOn;

            var rightOwner = input.RightBumper ? RightStickOwner.Feet
                : rightTriggerHeld ? RightStickOwner.RearFoot
                : RightStickOwner.Torso;
            var leftOwner = input.LeftBumper ? LeftStickOwner.BarSpin : LeftStickOwner.Hands;

            right.Update((int)rightOwner, input.Right.Value, dt, inputTuning.latch);
            left.Update((int)leftOwner, input.Left.Value, dt, inputTuning.latch);

            return new IntentFrame
            {
                Torso = right.Target((int)RightStickOwner.Torso),
                RearFoot = right.Target((int)RightStickOwner.RearFoot),
                Feet = right.Target((int)RightStickOwner.Feet),
                Hands = left.Target((int)LeftStickOwner.Hands),
                BarSpin = left.Target((int)LeftStickOwner.BarSpin).x,
                RightOwner = rightOwner,
                LeftOwner = leftOwner,

                Crouch = input.LeftTrigger,
                CrouchRate = input.LeftTriggerRate,

                HandsLoosened = input.LeftBumper,
                FeetFree = input.RightBumper,
                ScooterReleased = input.LeftBumper && input.RightBumper,

                PushHeld = input.Push,
                PushPressed = input.PushPressed,
                StanceSwitch = input.LeftStickClicked,
                ResetRequested = input.RightStickClicked,
                Camera = input.DPadPressed,

                RightStickVelocity = input.Right.Velocity,
                LeftStickVelocity = input.Left.Velocity,
                RightFlick = input.Right.Flick,
                LeftFlick = input.Left.Flick,
            };
        }
    }
}
