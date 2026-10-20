using ScootFoma.Input;
using ScootFoma.InputSources;
using ScootFoma.Intent;
using UnityEngine;

namespace ScootFoma.Debugging
{
    /// Отладочный экран слоя ввода и намерений: что пришло со стиков, что после фильтров,
    /// какие цели у эффекторов, кто владеет стиком, флики и запись.
    public sealed class IntentDebugHud : MonoBehaviour
    {
        [SerializeField] RiderInputDriver driver;
        [SerializeField] bool visible = true;
        [SerializeField] float stickRadius = 90f;

        static readonly Color RawColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color FilteredColor = Color.white;
        static readonly Color TorsoColor = new Color(0.3f, 0.8f, 1f);
        static readonly Color RearFootColor = new Color(1f, 0.55f, 0.2f);
        static readonly Color FeetColor = new Color(1f, 0.3f, 0.6f);
        static readonly Color HandsColor = new Color(0.4f, 1f, 0.4f);
        static readonly Color BarSpinColor = new Color(1f, 0.9f, 0.2f);

        GUIStyle label;

        void Awake()
        {
            if (driver == null) driver = FindAnyObjectByType<RiderInputDriver>();
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || driver == null) return;
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            var p = driver.Processed;
            var i = driver.Intent;

            float top = 20f, left = 20f;
            float d = stickRadius * 2f;
            var lsRect = new Rect(left, top + 40f, d, d);
            var rsRect = new Rect(left + d + 260f, top + 40f, d, d);

            GUI.Label(new Rect(left, top, 900, 22),
                $"<b>Ввод</b>  {(driver.IsReplaying ? ReplayLabel() : $"событий/с: {driver.GamepadEventsLastSecond}")}" +
                $"   {(driver.IsRecording ? $"<color=#ff5050>● ЗАПИСЬ {driver.RecordedFrames} тиков</color> (F5 — стоп)" : "F5 — записать")}   F1 — скрыть", label);

            // LS
            DrawStick(lsRect, "LS", p.Left);
            Dot(lsRect, i.Hands, HandsColor, 10f);
            if (i.LeftOwner == LeftStickOwner.BarSpin) Dot(lsRect, new Vector2(i.BarSpin, 0f), BarSpinColor, 10f);
            GUI.Label(new Rect(lsRect.xMax + 10f, lsRect.y, 250, 140),
                $"владелец: <b>{Owner(i.LeftOwner)}</b>\n" +
                $"<color=#66ff66>руки</color> {i.Hands.x:+0.00;-0.00} {i.Hands.y:+0.00;-0.00}\n" +
                $"<color=#ffe633>руль</color> {i.BarSpin:+0.00;-0.00}\n" +
                $"скорость {p.Left.Velocity.magnitude:F1} ед/с\n" +
                Flick(driver.LastLeftFlick, p.Time), label);

            // RS
            DrawStick(rsRect, "RS", p.Right);
            Dot(rsRect, i.Torso, TorsoColor, 10f);
            if (i.RightOwner == RightStickOwner.RearFoot) Dot(rsRect, i.RearFoot, RearFootColor, 10f);
            if (i.RightOwner == RightStickOwner.Feet) Dot(rsRect, i.Feet, FeetColor, 10f);
            GUI.Label(new Rect(rsRect.xMax + 10f, rsRect.y, 260, 140),
                $"владелец: <b>{Owner(i.RightOwner)}</b>\n" +
                $"<color=#4dccff>корпус</color> {i.Torso.x:+0.00;-0.00} {i.Torso.y:+0.00;-0.00}\n" +
                $"<color=#ff8c33>задняя нога</color> {i.RearFoot.x:+0.00;-0.00} {i.RearFoot.y:+0.00;-0.00}\n" +
                $"<color=#ff4d99>обе ноги</color> {i.Feet.x:+0.00;-0.00} {i.Feet.y:+0.00;-0.00}\n" +
                $"скорость {p.Right.Velocity.magnitude:F1} ед/с\n" +
                Flick(driver.LastRightFlick, p.Time), label);

            // Курки и кнопки
            float y = lsRect.yMax + 20f;
            Bar(new Rect(left, y, 200, 16), "LT присед", i.Crouch, $"{i.CrouchRate:+0.0;-0.0}/с");
            Bar(new Rect(left + 260, y, 200, 16), "RT", p.RightTrigger, i.RearFootActive ? "нога" : "");
            y += 26f;
            GUI.Label(new Rect(left, y, 900, 22),
                $"LB {On(i.HandsLoosened)} хват ослаблен   RB {On(i.FeetFree)} ноги свободны   " +
                $"A {On(i.PushHeld)} толчок   {(i.ScooterReleased ? "<b>самокат отпущен</b>" : "")}", label);
            y += 22f;
            GUI.Label(new Rect(left, y, 900, 22),
                $"Защёлка: {driver.Tuning.input.latch.mode}, растворение {driver.Tuning.input.latch.recenterTime:F2} с" +
                (string.IsNullOrEmpty(driver.LastSavedRecording) ? "" : $"   запись: {driver.LastSavedRecording}"), label);
        }

        string ReplayLabel()
        {
            float hz = 240f;
            int idx = driver.ReplayIndex, len = driver.ReplayLength;
            if (driver.ReplayFinished) return "<color=#ffd24d>ВОСПРОИЗВЕДЕНИЕ: запись закончилась</color>";
            return $"<color=#ffd24d>ВОСПРОИЗВЕДЕНИЕ {idx / hz:F1} / {len / hz:F1} с</color> (геймпад отключён)";
        }

        static string Owner(LeftStickOwner o) => o == LeftStickOwner.Hands ? "руки" : "руль (LB)";
        static string Owner(RightStickOwner o) => o switch
        {
            RightStickOwner.Torso => "корпус",
            RightStickOwner.RearFoot => "задняя нога (RT)",
            _ => "обе ноги (RB)",
        };

        static string On(bool b) => b ? "<color=#66ff66>■</color>" : "□";

        static string Flick((Flick flick, string owner, double time) f, double now)
        {
            if (!f.flick.Happened) return "флик: —";
            string age = now - f.time < 0.5 ? "<color=#ff5050>" : "<color=#999999>";
            return $"{age}флик → {f.owner}: {f.flick.Duration * 1000f:F0} мс, {f.flick.Speed:F0} ед/с</color>";
        }

        void DrawStick(Rect r, string name, StickState s)
        {
            var c = r.center;
            for (int k = 0; k < 64; k++)
            {
                float a = k / 64f * Mathf.PI * 2f;
                Pixel(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * stickRadius, new Color(1f, 1f, 1f, 0.25f), 2f);
            }
            Pixel(c, new Color(1f, 1f, 1f, 0.25f), 3f);
            GUI.Label(new Rect(r.x, r.y - 20f, 60, 20), name, label);
            Dot(r, s.Raw, RawColor, 6f);
            Dot(r, s.Value, FilteredColor, 6f);
        }

        void Dot(Rect r, Vector2 v, Color color, float size)
        {
            // Ось Y стика вверх, экранная — вниз.
            Pixel(r.center + new Vector2(v.x, -v.y) * stickRadius, color, size);
        }

        static void Pixel(Vector2 p, Color color, float size)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Bar(Rect r, string name, float value, string suffix)
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(r.xMax + 6f, r.y - 3f, 200, 22), $"{name} {value:F2} {suffix}", label);
        }
    }
}
