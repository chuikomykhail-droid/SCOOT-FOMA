using System.Collections.Generic;
using UnityEngine;

namespace ScootFoma.Simulation
{
    /// Единственный владелец шага физики. Физика шагает вручную (SimulationMode.Script),
    /// поэтому порядок «ввод → намерения → ассист → физика → сенсоры → распознавание» гарантирован.
    [DefaultExecutionOrder(-1000)]
    public sealed class SimulationLoop : MonoBehaviour
    {
        [SerializeField, Min(30f)] float physicsHz = 240f;
        [SerializeField, Min(1)] int maxTicksPerFrame = 16;
        [Tooltip("Скорость симуляции: 0.25 — замедление для разбора трюков, >1 — ускоренный прогон тестов.")]
        [SerializeField, Min(0f)] float speed = 1f;
        [SerializeField] bool paused;

        readonly List<ISimulationStep> steps = new List<ISimulationStep>();
        ISimulationStep[] stepsSnapshot = new ISimulationStep[0];
        bool stepsDirty;
        float accumulator;
        SimulationMode previousMode;

        public float Dt => 1f / physicsHz;
        public float PhysicsHz => physicsHz;
        public long Tick { get; private set; }
        public float SimulationTime => Tick * Dt;

        /// Доля шага, прошедшая после последнего Simulate. Для визуальной интерполяции.
        public float InterpolationAlpha { get; private set; }

        public float Speed { get => speed; set => speed = Mathf.Max(0f, value); }
        public bool Paused { get => paused; set => paused = value; }
        public int MaxTicksPerFrame { get => maxTicksPerFrame; set => maxTicksPerFrame = Mathf.Max(1, value); }

        public void SetPhysicsHz(float hz)
        {
            physicsHz = Mathf.Max(30f, hz);
            accumulator = 0f;
        }

        public void Register(ISimulationStep step)
        {
            if (step == null || steps.Contains(step)) return;
            steps.Add(step);
            stepsDirty = true;
        }

        public void Unregister(ISimulationStep step)
        {
            if (steps.Remove(step)) stepsDirty = true;
        }

        /// Один шаг вне зависимости от паузы — для покадровой отладки.
        public void StepOnce()
        {
            if (stepsDirty)
            {
                steps.Sort((a, b) => a.Order.CompareTo(b.Order));
                stepsSnapshot = steps.ToArray();
                stepsDirty = false;
            }

            float dt = Dt;
            var snapshot = stepsSnapshot;
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].PrePhysics(dt);
            Physics.Simulate(dt);
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].PostPhysics(dt);
            Tick++;
        }

        void OnEnable()
        {
            previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        void OnDisable()
        {
            Physics.simulationMode = previousMode;
        }

        void Update()
        {
            if (paused)
            {
                InterpolationAlpha = 1f;
                return;
            }

            float dt = Dt;
            accumulator += Time.unscaledDeltaTime * speed;
            int ticks = 0;
            while (accumulator >= dt && ticks < maxTicksPerFrame)
            {
                StepOnce();
                accumulator -= dt;
                ticks++;
            }

            // Не успеваем — теряем время, а не уходим в спираль смерти.
            if (ticks == maxTicksPerFrame && accumulator >= dt)
                accumulator = 0f;

            InterpolationAlpha = Mathf.Clamp01(accumulator / dt);
        }
    }
}
