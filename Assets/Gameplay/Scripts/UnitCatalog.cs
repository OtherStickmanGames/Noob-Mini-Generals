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

        // Казармы нанимают отряд целиком, как в Dawn of War (решение автора: 5 бойцов, без роста)
        public const string SquadName = "Отряд пехоты";
        public const int SquadSize = 5;
        public const int SquadCost = 250;
        public const float SquadHireTime = 25f;
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

        /// <summary>Сколько отрядов казармы могут держать в очереди найма</summary>
        public const int BarracksQueueLimit = 3;
    }
}
