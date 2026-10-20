using System;
using System.Collections.Generic;
using ScootFoma.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ScootFoma.InputSources
{
    /// Живой геймпад. Каждое событие сохраняется со своим временем (Input Actions типа PassThrough),
    /// а кадр тика собирается интерполяцией между событиями: геймпад шлёт ~120 событий/с, физика идёт на 240 Гц.
    /// Кадр берётся с задержкой sampleLag, чтобы между событиями было что интерполировать.
    public sealed class GamepadInputSource : IInputSource, IDisposable
    {
        sealed class Track
        {
            readonly List<double> times = new List<double>(64);
            readonly List<Vector2> values = new List<Vector2>(64);
            public Vector2 Latest => values.Count > 0 ? values[values.Count - 1] : Vector2.zero;

            public void Add(double time, Vector2 value)
            {
                // События приходят по порядку; на всякий случай держим порядок.
                if (times.Count > 0 && time < times[times.Count - 1]) time = times[times.Count - 1];
                times.Add(time);
                values.Add(value);
            }

            /// Линейная интерполяция по времени; до первого события — ноль, после последнего — последнее значение.
            public Vector2 Interpolate(double time)
            {
                int n = times.Count;
                if (n == 0) return Vector2.zero;
                if (time >= times[n - 1]) return values[n - 1];
                for (int i = n - 1; i > 0; i--)
                {
                    if (times[i - 1] <= time)
                    {
                        double span = times[i] - times[i - 1];
                        float a = span > 1e-9 ? (float)((time - times[i - 1]) / span) : 1f;
                        return Vector2.LerpUnclamped(values[i - 1], values[i], a);
                    }
                }
                return values[0];
            }

            /// Значение кнопки на момент time или нажатие, случившееся в (from, time]: короткий тап не теряется.
            public bool Held(double from, double time)
            {
                int n = times.Count;
                bool held = false;
                for (int i = 0; i < n; i++)
                {
                    if (times[i] <= time) held = values[i].x > 0.5f;
                    if (times[i] > from && times[i] <= time && values[i].x > 0.5f) return true;
                }
                return held;
            }

            /// Удаляет события старше keepAfter, оставляя одно предшествующее — опору для интерполяции.
            public void Prune(double keepAfter)
            {
                int remove = 0;
                while (remove + 1 < times.Count && times[remove + 1] < keepAfter) remove++;
                if (remove > 0)
                {
                    times.RemoveRange(0, remove);
                    values.RemoveRange(0, remove);
                }
            }

            public int CountSince(double time)
            {
                int c = 0;
                for (int i = times.Count - 1; i >= 0 && times[i] > time; i--) c++;
                return c;
            }
        }

        readonly InputActionMap map;
        readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();
        readonly List<(InputAction action, Action<InputAction.CallbackContext> handler)> subscriptions =
            new List<(InputAction, Action<InputAction.CallbackContext>)>();
        readonly double sampleLag;
        double lastSampleInputTime = double.NegativeInfinity;
        float savedDeadzoneMin, savedDeadzoneMax;

        public GamepadInputSource(InputActionAsset actions, double sampleLag = 1.0 / 120.0)
        {
            this.sampleLag = sampleLag;
            map = actions.FindActionMap("Rider", throwIfNotFound: true);
            foreach (var action in map.actions)
            {
                var track = new Track();
                tracks[action.name] = track;
                bool isVector = action.expectedControlType == "Vector2";
                Action<InputAction.CallbackContext> handler = ctx =>
                {
                    Vector2 v = isVector ? ctx.ReadValue<Vector2>() : new Vector2(ctx.ReadValue<float>(), 0f);
                    track.Add(ctx.time, v);
                };
                action.performed += handler;
                action.canceled += handler;
                subscriptions.Add((action, handler));
            }

            // Свои мёртвые зоны и флик считаем сами по сырым значениям: встроенную мёртвую зону стиков отключаем.
            savedDeadzoneMin = InputSystem.settings.defaultDeadzoneMin;
            savedDeadzoneMax = InputSystem.settings.defaultDeadzoneMax;
            InputSystem.settings.defaultDeadzoneMin = 0f;
            InputSystem.settings.defaultDeadzoneMax = 1f;
            map.Enable();
        }

        /// Событий за последнюю секунду по самому активному органу управления — для HUD.
        public int EventsLastSecond
        {
            get
            {
                double now = InputState.currentTime;
                int max = 0;
                foreach (var t in tracks.Values) max = Mathf.Max(max, t.CountSince(now - 1.0));
                return max;
            }
        }

        public InputFrame Sample(double tickRealtime)
        {
            // Время событий — в базе InputState.currentTime; переводим реальное время тика в неё.
            double offset = InputState.currentTime - Time.realtimeSinceStartupAsDouble;
            double t = tickRealtime + offset - sampleLag;
            double from = lastSampleInputTime;
            lastSampleInputTime = t;

            var frame = new InputFrame
            {
                LeftStick = tracks["LeftStick"].Interpolate(t),
                RightStick = tracks["RightStick"].Interpolate(t),
                LeftTrigger = tracks["LeftTrigger"].Interpolate(t).x,
                RightTrigger = tracks["RightTrigger"].Interpolate(t).x,
                LeftBumper = tracks["LeftBumper"].Held(from, t),
                RightBumper = tracks["RightBumper"].Held(from, t),
                Push = tracks["Push"].Held(from, t),
                LeftStickPress = tracks["LeftStickPress"].Held(from, t),
                RightStickPress = tracks["RightStickPress"].Held(from, t),
                DPad = tracks["DPad"].Interpolate(t),
            };

            // Держим историю ~0.5 с (для HUD и интерполяции); старое выбрасываем.
            foreach (var track in tracks.Values) track.Prune(Math.Min(t, InputState.currentTime - 1.0));
            return frame;
        }

        public void Dispose()
        {
            foreach (var (action, handler) in subscriptions)
            {
                action.performed -= handler;
                action.canceled -= handler;
            }
            subscriptions.Clear();
            map.Disable();
            InputSystem.settings.defaultDeadzoneMin = savedDeadzoneMin;
            InputSystem.settings.defaultDeadzoneMax = savedDeadzoneMax;
        }
    }
}
