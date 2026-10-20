using System.Collections.Generic;
using NUnit.Framework;
using ScootFoma.Data;
using ScootFoma.Input;
using ScootFoma.Intent;
using UnityEngine;

namespace ScootFoma.Tests
{
    public class StickMathTests
    {
        [Test]
        public void InsideDeadzone_IsZero()
        {
            Assert.AreEqual(Vector2.zero, StickMath.ApplyDeadzone(new Vector2(0.05f, 0.05f), new StickTuning()));
        }

        [Test]
        public void Output_IsContinuousAtDeadzoneEdge_AndReachesOne()
        {
            var t = new StickTuning { innerDeadzone = 0.1f, outerDeadzone = 0.95f };
            Assert.Less(StickMath.ApplyDeadzone(new Vector2(0.101f, 0f), t).magnitude, 0.01f);
            Assert.AreEqual(1f, StickMath.ApplyDeadzone(new Vector2(0.96f, 0f), t).magnitude, 1e-4f);
        }

        [Test]
        public void Direction_IsPreserved()
        {
            Vector2 v = StickMath.ApplyDeadzone(new Vector2(0.3f, 0.4f), new StickTuning());
            Assert.AreEqual(0.6f, v.x / v.magnitude, 1e-4f);
        }
    }

    public class FlickTests
    {
        const float Dt = 1f / 240f;

        /// Мах с упора на противоположный упор за duration секунд, по тикам 240 Гц.
        static List<Flick> Swipe(float from, float to, float duration, FlickTuning t)
        {
            var d = new FlickDetector();
            var flicks = new List<Flick>();
            double time = 0;
            for (int i = 0; i < 10; i++) { time += Dt; d.Update(new Vector2(from, 0f), time, t); }
            int steps = Mathf.Max(1, Mathf.RoundToInt(duration / Dt));
            for (int i = 1; i <= steps; i++)
            {
                time += Dt;
                var f = d.Update(new Vector2(Mathf.Lerp(from, to, i / (float)steps), 0f), time, t);
                if (f.Happened) flicks.Add(f);
            }
            return flicks;
        }

        [Test]
        public void FastSwipeThroughCenter_IsFlick_WithDirection()
        {
            var flicks = Swipe(-1f, 1f, 0.06f, new FlickTuning());
            Assert.AreEqual(1, flicks.Count);
            Assert.Greater(flicks[0].Direction.x, 0.9f);
        }

        [Test]
        public void SlowSwipe_IsNotFlick()
        {
            Assert.AreEqual(0, Swipe(-1f, 1f, 0.4f, new FlickTuning()).Count);
        }

        [Test]
        public void SameSideMovement_IsNotFlick()
        {
            Assert.AreEqual(0, Swipe(0.8f, 1f, 0.03f, new FlickTuning()).Count);
        }
    }

    public class StickChannelTests
    {
        [Test]
        public void StickClick_FreezesAxisForFreezeTime()
        {
            var ch = new StickChannel();
            var tuning = new InputTuning { clickFreezeTime = 0.05f };
            double t = 0;
            const float dt = 1f / 240f;
            ch.Update(new Vector2(0.5f, 0f), false, t += dt, tuning.leftStick, tuning);
            // Клик смещает ось вниз — значение должно остаться прежним.
            var s = ch.Update(new Vector2(0.5f, -0.3f), true, t += dt, tuning.leftStick, tuning);
            Assert.AreEqual(new Vector2(0.5f, 0f), s.Raw);
            for (int i = 0; i < 20; i++) s = ch.Update(new Vector2(0.5f, -0.3f), true, t += dt, tuning.leftStick, tuning);
            Assert.AreEqual(new Vector2(0.5f, -0.3f), s.Raw);
        }
    }

    public class StickRouterTests
    {
        const float Dt = 1f / 240f;
        static readonly LatchTuning NoRecenter = new LatchTuning { mode = LatchMode.Offset, recenterTime = 0f };

        [Test]
        public void Offset_NewOwnerGetsNoJump_OldOwnerHoldsPose()
        {
            var r = new StickRouter(false, true);
            r.Update(0, new Vector2(-0.8f, 0f), Dt, NoRecenter); // заводка корпуса
            r.Update(1, new Vector2(-0.8f, 0f), Dt, NoRecenter); // зажали RT со стиком на упоре
            Assert.AreEqual(Vector2.zero, r.Target(1), "нога не дёргается в момент нажатия");
            Assert.AreEqual(new Vector2(-0.8f, 0f), r.Target(0), "корпус держит позу");

            r.Update(1, new Vector2(0.2f, 0f), Dt, NoRecenter); // мах вправо
            Assert.AreEqual(1f, r.Target(1).x, 1e-5f);
            Assert.AreEqual(new Vector2(-0.8f, 0f), r.Target(0), "корпус всё ещё держит позу");
        }

        [Test]
        public void Offset_Recenters_TowardStick()
        {
            var t = new LatchTuning { mode = LatchMode.Offset, recenterTime = 0.1f };
            var r = new StickRouter(false, false);
            r.Update(0, new Vector2(0.5f, 0f), Dt, t);
            r.Update(1, new Vector2(0.5f, 0f), Dt, t);
            for (int i = 0; i < 240; i++) r.Update(1, new Vector2(0.5f, 0f), Dt, t);
            Assert.AreEqual(0.5f, r.Target(1).x, 0.01f);
        }

        [Test]
        public void WaitCenter_HoldsUntilStickReturns()
        {
            var t = new LatchTuning { mode = LatchMode.WaitCenter, centerRadius = 0.15f };
            var r = new StickRouter(false, false);
            r.Update(0, new Vector2(0.9f, 0f), Dt, t);
            r.Update(1, new Vector2(0.9f, 0f), Dt, t);
            Assert.AreEqual(Vector2.zero, r.Target(1));
            r.Update(1, new Vector2(0.05f, 0f), Dt, t);
            r.Update(1, new Vector2(-0.7f, 0f), Dt, t);
            Assert.AreEqual(-0.7f, r.Target(1).x, 1e-5f);
        }

        [Test]
        public void TemporaryOwner_ResetsAfterRelease()
        {
            var r = new StickRouter(false, true);
            r.Update(0, Vector2.zero, Dt, NoRecenter);
            r.Update(1, Vector2.zero, Dt, NoRecenter);
            r.Update(1, new Vector2(1f, 0f), Dt, NoRecenter);
            r.Update(0, new Vector2(1f, 0f), Dt, NoRecenter);
            Assert.AreEqual(Vector2.zero, r.Target(1));
        }
    }

    public class IntentBuilderTests
    {
        const float Dt = 1f / 240f;

        static IntentFrame Run(IEnumerable<InputFrame> frames, out List<IntentFrame> all)
        {
            var processor = new InputFilter();
            var builder = new IntentBuilder();
            var tuning = new InputTuning();
            var intentTuning = new IntentTuning();
            all = new List<IntentFrame>();
            IntentFrame last = default;
            foreach (var f in frames)
            {
                var p = processor.Process(f, Dt, tuning);
                last = builder.Build(p, Dt, tuning, intentTuning);
                all.Add(last);
            }
            return last;
        }

        [Test]
        public void RightTrigger_RoutesRightStickToRearFoot_TorsoHolds()
        {
            var frames = new List<InputFrame>();
            for (int i = 0; i < 20; i++) frames.Add(new InputFrame { RightStick = new Vector2(-0.9f, 0f) });
            for (int i = 0; i < 5; i++) frames.Add(new InputFrame { RightStick = new Vector2(-0.9f, 0f), RightTrigger = 1f });
            var last = Run(frames, out var all);
            Assert.AreEqual(RightStickOwner.RearFoot, last.RightOwner);
            Assert.Less(last.Torso.x, -0.8f, "корпус держит заводку");
            // В тик нажатия нога почти в нейтрали; дальше смещение защёлки растворяется (recenterTime) — это задумано.
            Assert.Less(all[20].RearFoot.magnitude, 0.02f, "нога без рывка в момент нажатия RT");
        }

        [Test]
        public void RightBumper_HasPriorityOverRightTrigger()
        {
            var last = Run(new[] { new InputFrame { RightTrigger = 1f, RightBumper = true } }, out _);
            Assert.AreEqual(RightStickOwner.Feet, last.RightOwner);
        }

        [Test]
        public void RightTrigger_HasHysteresis()
        {
            var frames = new[]
            {
                new InputFrame { RightTrigger = 0.7f },
                new InputFrame { RightTrigger = 0.5f }, // между порогами — остаётся зажатым
                new InputFrame { RightTrigger = 0.3f },
            };
            Run(frames, out var all);
            Assert.IsTrue(all[0].RearFootActive);
            Assert.IsTrue(all[1].RearFootActive);
            Assert.IsFalse(all[2].RearFootActive);
        }

        [Test]
        public void SameFrames_GiveIdenticalIntents()
        {
            var frames = new List<InputFrame>();
            for (int i = 0; i < 200; i++)
            {
                float a = i * 0.07f;
                frames.Add(new InputFrame
                {
                    LeftStick = new Vector2(Mathf.Sin(a), Mathf.Cos(a * 1.3f)) * 0.9f,
                    RightStick = new Vector2(Mathf.Cos(a * 2f), Mathf.Sin(a)) * 0.95f,
                    LeftTrigger = Mathf.PingPong(a, 1f),
                    RightTrigger = i % 60 < 30 ? 1f : 0f,
                    LeftBumper = i % 90 < 20,
                });
            }
            Run(frames, out var first);
            Run(frames, out var second);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Torso, second[i].Torso);
                Assert.AreEqual(first[i].RearFoot, second[i].RearFoot);
                Assert.AreEqual(first[i].Hands, second[i].Hands);
                Assert.AreEqual(first[i].RightFlick.Happened, second[i].RightFlick.Happened);
            }
        }
    }
}
