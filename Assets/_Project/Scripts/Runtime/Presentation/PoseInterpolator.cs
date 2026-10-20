using System.Collections.Generic;
using ScootFoma.Simulation;
using UnityEngine;

namespace ScootFoma.Presentation
{
    /// Визуальная интерполяция между двумя последними шагами физики.
    /// У ArticulationBody нет встроенной интерполяции, а шаг физики не совпадает с кадром,
    /// поэтому визуал живёт отдельно от физических тел и сглаживается здесь.
    [DefaultExecutionOrder(900)]
    public sealed class PoseInterpolator : MonoBehaviour, ISimulationStep
    {
        [SerializeField] SimulationLoop loop;
        [SerializeField] bool interpolate = true;

        struct Entry
        {
            public Transform Source;
            public Transform Visual;
            public Vector3 PrevPosition, CurrPosition;
            public Quaternion PrevRotation, CurrRotation;
        }

        readonly List<Entry> entries = new List<Entry>();

        public int Order => SimulationOrder.Presentation;
        public bool Interpolate { get => interpolate; set => interpolate = value; }

        public void Track(Transform source, Transform visual)
        {
            source.GetPositionAndRotation(out var p, out var r);
            entries.Add(new Entry
            {
                Source = source, Visual = visual,
                PrevPosition = p, CurrPosition = p, PrevRotation = r, CurrRotation = r,
            });
            visual.SetPositionAndRotation(p, r);
        }

        void Awake()
        {
            if (loop == null) loop = FindAnyObjectByType<SimulationLoop>();
        }

        void OnEnable() { if (loop != null) loop.Register(this); }
        void OnDisable() { if (loop != null) loop.Unregister(this); }

        public void PrePhysics(float dt) { }

        public void PostPhysics(float dt)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.Source == null || e.Visual == null)
                {
                    if (e.Visual != null) Destroy(e.Visual.gameObject);
                    entries.RemoveAt(i);
                    continue;
                }
                e.PrevPosition = e.CurrPosition;
                e.PrevRotation = e.CurrRotation;
                e.Source.GetPositionAndRotation(out e.CurrPosition, out e.CurrRotation);
                entries[i] = e;
            }
        }

        void LateUpdate()
        {
            float alpha = interpolate && loop != null ? loop.InterpolationAlpha : 1f;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Visual == null) continue;
                e.Visual.SetPositionAndRotation(
                    Vector3.LerpUnclamped(e.PrevPosition, e.CurrPosition, alpha),
                    Quaternion.SlerpUnclamped(e.PrevRotation, e.CurrRotation, alpha));
            }
        }
    }
}
