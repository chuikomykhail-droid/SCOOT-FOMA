using ScootFoma.Input;
using UnityEngine;

namespace ScootFoma.Intent
{
    /// Один стик — несколько эффекторов, модификатор выбирает активный. Защёлка (спецификация, «Правила ввода»):
    /// прежний эффектор держит последнюю цель, новый не получает рывка в момент переключения.
    public sealed class StickRouter
    {
        readonly Vector2[] targets;
        readonly Vector2[] offsets;
        readonly bool[] waiting;
        readonly bool[] resetOnRelease;
        int active = -1;

        /// resetOnRelease[i] — эффектор временный (нога для випа, раскрутка руля): после отпускания
        /// модификатора его цель обнуляется, чтобы следующее включение начиналось из нейтрали.
        public StickRouter(params bool[] resetOnRelease)
        {
            int n = resetOnRelease.Length;
            targets = new Vector2[n];
            offsets = new Vector2[n];
            waiting = new bool[n];
            this.resetOnRelease = resetOnRelease;
        }

        public int Active => active;
        public bool SwitchedThisTick { get; private set; }
        public Vector2 Target(int owner) => targets[owner];

        public void Reset()
        {
            active = -1;
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = offsets[i] = Vector2.zero;
                waiting[i] = false;
            }
        }

        public void Update(int owner, Vector2 stick, float dt, LatchTuning t)
        {
            SwitchedThisTick = false;
            if (active < 0)
            {
                active = owner;
            }
            else if (owner != active)
            {
                if (resetOnRelease[active]) targets[active] = Vector2.zero;
                active = owner;
                SwitchedThisTick = true;
                if (t.mode == LatchMode.Offset) offsets[active] = targets[active] - stick;
                else waiting[active] = true;
            }

            if (t.mode == LatchMode.Offset)
            {
                // Смещение растворяется, и цель плавно возвращается к прямому соответствию стику.
                if (t.recenterTime > 0f) offsets[active] *= Mathf.Exp(-dt / t.recenterTime);
                targets[active] = StickMath.ClampToUnitDisk(stick + offsets[active]);
            }
            else
            {
                if (waiting[active] && stick.magnitude <= t.centerRadius) waiting[active] = false;
                if (!waiting[active]) targets[active] = stick;
            }
        }
    }
}
