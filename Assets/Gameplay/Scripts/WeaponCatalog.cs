namespace Generals
{
    public enum ProjectileKind
    {
        /// <summary>Пуля: тонкий трассер, попадание — урон одной цели</summary>
        Bullet,
        /// <summary>Снаряд: взрывается, урон по площади</summary>
        Shell,
    }

    public class WeaponDef
    {
        public string name;
        public ProjectileKind kind;
        /// <summary>Урон прямого попадания</summary>
        public float damage;
        /// <summary>Секунд между выстрелами</summary>
        public float interval;
        /// <summary>Дальность стрельбы, м</summary>
        public float range;
        /// <summary>Скорость снаряда, м/с</summary>
        public float projectileSpeed;
        /// <summary>Доля выстрелов в цель; промах уходит в землю рядом с целью</summary>
        public float accuracy;
        /// <summary>Разброс ствола, градусы (и у попаданий, и у промахов)</summary>
        public float spread;
        /// <summary>Радиус взрыва, м; 0 — без взрыва</summary>
        public float splashRadius;
        /// <summary>Радиус воронки, м</summary>
        public float craterRadius;
    }

    /// <summary>
    /// Оружие вертикального среза. Цифры предварительные, баланс позже.
    /// </summary>
    public static class WeaponCatalog
    {
        public static readonly WeaponDef Rifle = new()
        {
            name = "Винтовка",
            kind = ProjectileKind.Bullet,
            damage = 9f,
            interval = 1f,
            range = UnitCatalog.InfantryRange,
            projectileSpeed = 38f,
            accuracy = 0.7f,
            spread = 1.2f,
            // Пуля выбивает из земли один воксель
            craterRadius = 0.35f,
        };

        public static readonly WeaponDef Cannon = new()
        {
            name = "Пушка турели",
            kind = ProjectileKind.Shell,
            damage = 45f,
            interval = 2.2f,
            range = 15f,
            projectileSpeed = 24f,
            accuracy = 0.8f,
            spread = 1f,
            splashRadius = 1.8f,
            craterRadius = 0.9f,
        };

        /// <summary>Скорость поворота башни турели, градусы в секунду</summary>
        public const float TurretTurnSpeed = 200f;
        /// <summary>Турель стреляет, когда ствол смотрит на цель точнее этого, градусы</summary>
        public const float TurretAimTolerance = 6f;
    }
}
