using System;
using System.Collections.Generic;
using ScootFoma.Data;
using ScootFoma.Input;
using ScootFoma.Intent;
using ScootFoma.Settings;
using ScootFoma.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScootFoma.InputSources
{
    /// Первый шаг тика: источник ввода → InputFilter → IntentBuilder. Результат (Intent) читают
    /// следующие слои. Сам ничего не двигает.
    public sealed class RiderInputDriver : MonoBehaviour, ISimulationStep
    {
        [SerializeField] SimulationLoop loop;
        [SerializeField] InputActionAsset actions;
        [SerializeField] InputTuningAsset tuning;
        [Tooltip("Если задано — вместо геймпада воспроизводится запись.")]
        [SerializeField] InputRecording replay;
        [Tooltip("Зациклить воспроизведение. Иначе после конца записи ввод остаётся нулевым.")]
        [SerializeField] bool loopReplay;
        [Tooltip("Клавиша начала/остановки записи ввода (в редакторе запись сохраняется ассетом).")]
        [SerializeField] Key recordKey = Key.F5;
        [SerializeField] string recordingsFolder = "Assets/_Project/Recordings";

        readonly InputFilter processor = new InputFilter();
        readonly IntentBuilder builder = new IntentBuilder();
        IInputSource source;
        GamepadInputSource gamepad;
        List<InputFrame> recordingBuffer;
        bool replayEndLogged;

        public InputFrame Frame { get; private set; }
        public ProcessedInput Processed { get; private set; }
        public IntentFrame Intent { get; private set; }
        public bool IsRecording => recordingBuffer != null;
        public int RecordedFrames => recordingBuffer?.Count ?? 0;
        public bool IsReplaying => replay != null;
        public int ReplayIndex => (source as RecordingInputSource)?.Index ?? 0;
        public int ReplayLength => (source as RecordingInputSource)?.Length ?? 0;
        public bool ReplayFinished => (source as RecordingInputSource)?.Finished ?? false;
        public int GamepadEventsLastSecond => gamepad?.EventsLastSecond ?? 0;
        public string LastSavedRecording { get; private set; }

        /// Последний флик по каждому стику (для HUD: держится, пока не случится следующий).
        public (Flick flick, string owner, double time) LastRightFlick { get; private set; }
        public (Flick flick, string owner, double time) LastLeftFlick { get; private set; }

        public int Order => SimulationOrder.Input;
        public InputTuningAsset Tuning => tuning;

        void Awake()
        {
            if (loop == null) loop = FindAnyObjectByType<SimulationLoop>();
            if (tuning == null) tuning = ScriptableObject.CreateInstance<InputTuningAsset>();
        }

        void OnEnable()
        {
            if (replay != null)
            {
                source = new RecordingInputSource(replay);
                if (loop != null && !Mathf.Approximately(replay.physicsHz, loop.PhysicsHz))
                    Debug.LogWarning($"[Input] Запись сделана на {replay.physicsHz} Гц, физика {loop.PhysicsHz} Гц — тики не совпадут");
            }
            else if (actions != null)
            {
                gamepad = new GamepadInputSource(actions);
                source = gamepad;
            }
            else
            {
                Debug.LogError("[Input] Не задан ни InputActionAsset, ни запись");
            }
            processor.Reset();
            builder.Reset();
            loop?.Register(this);
            Debug.Log($"[Input] источник: {(replay != null ? $"запись {replay.name} ({replay.Duration:F1} с)" : "геймпад")}");
        }

        void OnDisable()
        {
            loop?.Unregister(this);
            gamepad?.Dispose();
            gamepad = null;
            source = null;
        }

        public void PrePhysics(float dt)
        {
            if (source == null) return;
            if (source is RecordingInputSource rec && rec.Finished)
            {
                if (loopReplay) { rec.Restart(); processor.Reset(); builder.Reset(); }
                else if (!replayEndLogged)
                {
                    replayEndLogged = true;
                    Debug.Log("[Input] Запись закончилась: дальше ввод нулевой (включите Loop Replay, чтобы повторять)");
                }
            }
            var frame = source.Sample(loop.CurrentTickRealtime);
            recordingBuffer?.Add(frame);

            var processed = processor.Process(frame, dt, tuning.input);
            var intent = builder.Build(processed, dt, tuning.input, tuning.intent);
            Frame = frame;
            Processed = processed;
            Intent = intent;

            if (intent.RightFlick.Happened) LastRightFlick = (intent.RightFlick, intent.RightOwner.ToString(), processed.Time);
            if (intent.LeftFlick.Happened) LastLeftFlick = (intent.LeftFlick, intent.LeftOwner.ToString(), processed.Time);
        }

        public void PostPhysics(float dt) { }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || replay != null) return;
            if (keyboard[recordKey].wasPressedThisFrame)
            {
                if (IsRecording) StopRecording();
                else recordingBuffer = new List<InputFrame>(4096);
            }
        }

        void StopRecording()
        {
            var asset = ScriptableObject.CreateInstance<InputRecording>();
            asset.physicsHz = loop.PhysicsHz;
            asset.frames = recordingBuffer;
            recordingBuffer = null;
#if UNITY_EDITOR
            System.IO.Directory.CreateDirectory(recordingsFolder);
            string path = $"{recordingsFolder}/Input_{DateTime.Now:yyyyMMdd_HHmmss}.asset";
            UnityEditor.AssetDatabase.CreateAsset(asset, path);
            UnityEditor.AssetDatabase.SaveAssets();
            LastSavedRecording = path;
            Debug.Log($"[Input] Запись сохранена: {path} ({asset.frames.Count} тиков, {asset.Duration:F1} с)");
#else
            LastSavedRecording = "(в билде запись не сохраняется)";
#endif
        }
    }
}
