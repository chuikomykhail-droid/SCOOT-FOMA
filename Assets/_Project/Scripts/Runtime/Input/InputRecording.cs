using System.Collections.Generic;
using ScootFoma.Data;
using UnityEngine;

namespace ScootFoma.InputSources
{
    /// Запись ввода по тикам. Воспроизводится с той же частотой физики — обработка ввода и намерений
    /// при этом повторяется побитово, что делает запись основой регрессионных тестов трюков.
    [CreateAssetMenu(menuName = "Scoot Foma/Input Recording", fileName = "InputRecording")]
    public sealed class InputRecording : ScriptableObject
    {
        public float physicsHz = 240f;
        public List<InputFrame> frames = new List<InputFrame>();

        public float Duration => physicsHz > 0f ? frames.Count / physicsHz : 0f;
    }

    /// Воспроизведение записи: кадр за кадром по номеру тика, время тика не используется.
    public sealed class RecordingInputSource : IInputSource
    {
        readonly InputRecording recording;
        int index;

        public RecordingInputSource(InputRecording recording) => this.recording = recording;

        public bool Finished => index >= recording.frames.Count;
        public int Index => index;
        public int Length => recording.frames.Count;
        public void Restart() => index = 0;

        public InputFrame Sample(double tickRealtime) =>
            index < recording.frames.Count ? recording.frames[index++] : default;
    }
}
