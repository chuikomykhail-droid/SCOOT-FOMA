using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ScootFoma.Spikes
{
    /// Спайк S4: реальная частота событий геймпада и ловля флика (мах стиком через центр).
    /// Каждое событие читается отдельно через onEvent, а не последнее значение за кадр.
    public sealed class S4InputSpike : MonoBehaviour
    {
        [SerializeField] float pollingFrequency = 250f;
        [Tooltip("Порог «стик на упоре» для флика.")]
        [SerializeField, Range(0.3f, 1f)] float flickThreshold = 0.8f;
        [SerializeField] float graphSeconds = 1f;

        struct Sample
        {
            public double Time;
            public Vector2 LS, RS;
            public float LT, RT;
        }

        readonly Sample[] ring = new Sample[4096];
        int head, count;
        Sample last;

        // Флик по RS.x: от упора одной стороны до упора другой
        double flickStartTime = -1;
        int flickStartSide;
        int flickStartIndex;
        string lastFlick = "—";
        float maxStickSpeed;

        string deviceName = "нет геймпада";
        float defaultPolling;

        void OnEnable()
        {
            defaultPolling = InputSystem.pollingFrequency;
            InputSystem.pollingFrequency = pollingFrequency;
            InputSystem.onEvent += OnEvent;
        }

        void OnDisable()
        {
            InputSystem.onEvent -= OnEvent;
            InputSystem.pollingFrequency = defaultPolling;
        }

        void OnEvent(InputEventPtr e, InputDevice device)
        {
            if (!(device is Gamepad pad)) return;
            if (!e.IsA<StateEvent>() && !e.IsA<DeltaStateEvent>()) return;
            deviceName = device.displayName;

            var s = last;
            s.Time = e.time;
            if (pad.leftStick.ReadValueFromEvent(e, out Vector2 ls)) s.LS = ls;
            if (pad.rightStick.ReadValueFromEvent(e, out Vector2 rs)) s.RS = rs;
            if (pad.leftTrigger.ReadValueFromEvent(e, out float lt)) s.LT = lt;
            if (pad.rightTrigger.ReadValueFromEvent(e, out float rt)) s.RT = rt;

            if (count > 0)
            {
                double dt = s.Time - last.Time;
                if (dt > 1e-5) maxStickSpeed = Mathf.Max(maxStickSpeed, (float)((s.RS - last.RS).magnitude / dt));
            }
            TrackFlick(s);

            ring[head] = s;
            head = (head + 1) % ring.Length;
            if (count < ring.Length) count++;
            last = s;
        }

        void TrackFlick(Sample s)
        {
            int side = s.RS.x > flickThreshold ? 1 : s.RS.x < -flickThreshold ? -1 : 0;
            if (side == 0) return;
            if (flickStartSide == -side && flickStartTime >= 0)
            {
                double ms = (s.Time - flickStartTime) * 1000.0;
                int samples = count - flickStartIndex;
                if (ms < 300)
                {
                    lastFlick = $"{ms:F0} мс, {samples} событий";
                    Debug.Log($"[S4] флик RS: {lastFlick}");
                }
            }
            // Начало флика — последний момент на упоре этой стороны
            flickStartSide = side;
            flickStartTime = s.Time;
            flickStartIndex = count;
        }

        float EventRate()
        {
            if (count < 2) return 0f;
            double now = ring[(head - 1 + ring.Length) % ring.Length].Time;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - 1 - i + ring.Length * 2) % ring.Length];
                if (now - s.Time > 1.0) break;
                n++;
            }
            return n;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 700, 200));
            GUILayout.Label($"S4: {deviceName}   polling={InputSystem.pollingFrequency:F0} Гц   событий за последнюю секунду={EventRate():F0}");
            GUILayout.Label($"Последний флик: {lastFlick}   макс. скорость RS: {maxStickSpeed:F1} ед/с");
            GUILayout.Label($"LS={last.LS:F2} RS={last.RS:F2} LT={last.LT:F2} RT={last.RT:F2}");
            GUILayout.Label("Махните правым стиком влево-вправо через центр. Без движения стика событий почти нет — это нормально.");
            GUILayout.EndArea();

            // График RS.x за последние graphSeconds
            var area = new Rect(10, 220, 800, 200);
            GUI.Box(area, GUIContent.none);
            if (count < 2) return;
            double now = ring[(head - 1 + ring.Length) % ring.Length].Time;
            var dot = Texture2D.whiteTexture;
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - 1 - i + ring.Length * 2) % ring.Length];
                double age = now - s.Time;
                if (age > graphSeconds) break;
                float x = area.xMax - (float)(age / graphSeconds) * area.width;
                float y = area.center.y - s.RS.x * area.height * 0.45f;
                GUI.DrawTexture(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), dot);
            }
        }
    }
}
