using UnityEngine;

namespace ScootFoma.Input
{
    public struct StickState
    {
        /// После мёртвой зоны и кривой — то, чем управляется поза.
        public Vector2 Value;
        /// До мёртвой зоны (но после заморозки клика) — для флика и скорости.
        public Vector2 Raw;
        /// Скорость сырого стика, ед/с.
        public Vector2 Velocity;
        public Flick Flick;
    }

    /// Обработка одного стика по тикам: заморозка оси при клике, мёртвая зона, скорость, флик.
    public sealed class StickChannel
    {
        const int HistorySize = 64;

        readonly Vector2[] history = new Vector2[HistorySize];
        readonly double[] historyTime = new double[HistorySize];
        int head, count;

        readonly FlickDetector flick = new FlickDetector();
        bool lastPressed;
        bool hasPrevious;
        Vector2 previousRaw;
        double freezeUntil = double.NegativeInfinity;
        Vector2 frozen;

        public void Reset()
        {
            head = count = 0;
            flick.Reset();
            lastPressed = false;
            hasPrevious = false;
            freezeUntil = double.NegativeInfinity;
        }

        public StickState Update(Vector2 raw, bool pressed, double time, StickTuning stick, InputTuning tuning)
        {
            // Нажатие стика сдвигает ось: на время clickFreezeTime держим значение до клика.
            if (hasPrevious && pressed != lastPressed && tuning.clickFreezeTime > 0f)
            {
                freezeUntil = time + tuning.clickFreezeTime;
                frozen = previousRaw;
            }
            lastPressed = pressed;
            if (time < freezeUntil) raw = frozen;
            else previousRaw = raw;
            hasPrevious = true;

            Push(raw, time);
            return new StickState
            {
                Raw = raw,
                Value = StickMath.ApplyDeadzone(raw, stick),
                Velocity = Velocity(time, tuning.velocityWindow),
                Flick = flick.Update(raw, time, tuning.flick),
            };
        }

        void Push(Vector2 value, double time)
        {
            history[head] = value;
            historyTime[head] = time;
            head = (head + 1) % HistorySize;
            if (count < HistorySize) count++;
        }

        Vector2 Velocity(double now, float window)
        {
            if (count < 2) return Vector2.zero;
            int newest = (head - 1 + HistorySize) % HistorySize;
            int oldest = newest;
            for (int i = 1; i < count; i++)
            {
                int idx = (newest - i + HistorySize) % HistorySize;
                oldest = idx;
                if (now - historyTime[idx] >= window) break;
            }
            double dt = historyTime[newest] - historyTime[oldest];
            return dt > 1e-6 ? (history[newest] - history[oldest]) / (float)dt : Vector2.zero;
        }
    }
}
