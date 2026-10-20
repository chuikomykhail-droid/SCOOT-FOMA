using ScootFoma.Data;

namespace ScootFoma.InputSources
{
    /// Источник кадров ввода по тикам: живой геймпад или запись.
    public interface IInputSource
    {
        /// Кадр для тика, который представляет момент tickRealtime (Time.realtimeSinceStartupAsDouble).
        InputFrame Sample(double tickRealtime);
    }
}
