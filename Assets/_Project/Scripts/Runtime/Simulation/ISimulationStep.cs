namespace ScootFoma.Simulation
{
    /// Участник шага симуляции. Своих FixedUpdate у участников нет: порядок задаёт SimulationLoop.
    public interface ISimulationStep
    {
        /// Меньше — раньше. Опорные значения в SimulationOrder.
        int Order { get; }

        /// До Physics.Simulate: ввод, намерения, ассист, команды приводам и силы.
        void PrePhysics(float dt);

        /// После Physics.Simulate: сенсоры, распознавание, снапшоты.
        void PostPhysics(float dt);
    }

    public static class SimulationOrder
    {
        public const int Input = -400;
        public const int Intent = -300;
        public const int Assist = -200;
        public const int Actuation = -100;
        public const int Default = 0;
        public const int Sensing = 100;
        public const int Recognition = 200;
        public const int Presentation = 1000;
    }
}
