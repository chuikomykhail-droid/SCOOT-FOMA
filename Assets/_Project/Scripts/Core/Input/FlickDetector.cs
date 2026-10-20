using UnityEngine;

namespace ScootFoma.Input
{
    public struct Flick
    {
        public bool Happened;
        /// Направление маха — сторона, на которую стик пришёл (единичный вектор).
        public Vector2 Direction;
        /// От последнего момента на упоре до прихода на противоположный упор, с.
        public float Duration;
        /// Пройденный путь стика / длительность, ед/с.
        public float Speed;
    }

    /// Флик — быстрый мах стиком через центр: с упора на противоположный упор за короткое время.
    /// Работает на значениях до мёртвой зоны: мах проходит через центр, и мёртвая зона его бы обрезала.
    public sealed class FlickDetector
    {
        bool hasEdge;
        double edgeTime;
        Vector2 edgeDirection;
        Vector2 edgeValue;
        float minRadiusSinceEdge;

        public void Reset() => hasEdge = false;

        public Flick Update(Vector2 value, double time, FlickTuning t)
        {
            float r = value.magnitude;
            if (hasEdge) minRadiusSinceEdge = Mathf.Min(minRadiusSinceEdge, r);
            if (r < t.edge) return default;

            Vector2 direction = value / r;
            var flick = default(Flick);
            if (hasEdge)
            {
                double duration = time - edgeTime;
                bool opposite = Vector2.Dot(direction, edgeDirection) < -0.5f; // противоположный сектор ±60°
                if (opposite && minRadiusSinceEdge <= t.centerRadius && duration > 0 && duration <= t.maxDuration)
                {
                    flick = new Flick
                    {
                        Happened = true,
                        Direction = direction,
                        Duration = (float)duration,
                        Speed = (value - edgeValue).magnitude / (float)duration,
                    };
                }
            }

            // Отсчёт маха — от последнего момента на упоре, поэтому обновляем каждый тик на упоре.
            hasEdge = true;
            edgeTime = time;
            edgeDirection = direction;
            edgeValue = value;
            minRadiusSinceEdge = r;
            return flick;
        }
    }
}
