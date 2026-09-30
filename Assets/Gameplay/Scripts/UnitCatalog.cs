namespace Generals
{
    /// <summary>Как ведут себя бойцы казарм. Задаётся на казармы, действует на всех их бойцов.</summary>
    public enum BarracksBehavior
    {
        /// <summary>Держат пост у своих ворот, отбивают подошедших врагов</summary>
        Defend,
        /// <summary>Идут на врага: ближайшие вражеские бойцы, иначе здания противника</summary>
        Attack,
    }

    /// <summary>
    /// Боевые юниты вертикального среза. Цифры предварительные, баланс позже.
    /// </summary>
    public static class UnitCatalog
    {
        public const string InfantryName = "Пехотинец";
        public const int InfantryCost = 60;
        public const float InfantryHireTime = 10f;
        public const float InfantryHealth = 100f;
        public const float InfantrySpeed = 3.2f;

        /// <summary>Дальность стрельбы, м (стрельба — шаг 5 среза)</summary>
        public const float InfantryRange = 8f;
        /// <summary>На каком расстоянии боец замечает врага, м</summary>
        public const float InfantrySight = 14f;
        /// <summary>Защитник бросается на врага, подошедшего к посту ближе этого, м</summary>
        public const float DefendRadius = 18f;

        /// <summary>
        /// Атакующий выбирает цель случайно среди построек врага не дальше ближайшей больше чем на
        /// столько метров
        /// </summary>
        public const float AttackTargetWindow = 12f;

        /// <summary>Сколько бойцов казармы могут держать в очереди найма</summary>
        public const int BarracksQueueLimit = 5;
    }
}
