using ScootFoma.Input;
using ScootFoma.Intent;
using UnityEngine;

namespace ScootFoma.Settings
{
    /// Коэффициенты ввода и намерений. Правка в Play Mode сохраняется в ассет — это удобно для тюнинга;
    /// код в рантайме сюда никогда не пишет.
    [CreateAssetMenu(menuName = "Scoot Foma/Input Tuning", fileName = "InputTuning")]
    public sealed class InputTuningAsset : ScriptableObject
    {
        public InputTuning input = new InputTuning();
        public IntentTuning intent = new IntentTuning();
    }
}
