using System.Collections.Generic;

namespace Generals
{
    public enum VehicleType
    {
        Scout,
        Tank,
        Artillery,
    }

    public class VehicleDef
    {
        public VehicleType type;
        public string name;
        /// <summary>Коротко, против чего — в панели завода</summary>
        public string role;
        public int cost;
        /// <summary>Секунды производства на заводе</summary>
        public float buildTime;
        public float health;
        /// <summary>Скорость, м/с</summary>
        public float speed;
        /// <summary>Поворот корпуса, градусы в секунду</summary>
        public float hullTurnSpeed;
        /// <summary>Поворот башни, градусы в секунду</summary>
        public float turretTurnSpeed;
        public WeaponDef weapon;
        /// <summary>Стреляет только стоя (артиллерия разворачивается); остальные — и на ходу</summary>
        public bool firesOnlyStopped;
        /// <summary>Исследование в заводе; 0 — доступна сразу</summary>
        public int researchCost;
        public float researchTime;
    }

    /// <summary>
    /// Техника вертикального среза (решение автора: набор как в Dawn of War — разведчик, танк,
    /// артиллерия; управление как у казарм). Цифры предварительные, баланс позже.
    /// </summary>
    public static class VehicleCatalog
    {
        /// <summary>Сколько машин завод держит в очереди производства</summary>
        public const int FactoryQueueLimit = 3;
        /// <summary>На каком расстоянии машина замечает врага, м</summary>
        public const float Sight = 18f;
        /// <summary>Машина в обороне выезжает на врага, подошедшего к посту ближе этого, м</summary>
        public const float DefendRadius = 20f;

        static readonly Dictionary<VehicleType, VehicleDef> defs = new()
        {
            [VehicleType.Scout] = new VehicleDef
            {
                type = VehicleType.Scout,
                name = "Разведчик",
                role = "быстрый, против пехоты",
                cost = 200,
                buildTime = 20f,
                health = 380f,
                speed = 6f,
                hullTurnSpeed = 240f,
                turretTurnSpeed = 300f,
                weapon = new WeaponDef
                {
                    name = "Спаренный пулемёт",
                    kind = ProjectileKind.Bullet,
                    damage = 6f,
                    interval = 0.16f,
                    range = 10f,
                    projectileSpeed = 42f,
                    accuracy = 0.55f,
                    spread = 2.5f,
                    craterRadius = 0.3f,
                    structureDamage = 0.3f,
                    vehicleDamage = 0.3f,
                },
            },
            [VehicleType.Tank] = new VehicleDef
            {
                type = VehicleType.Tank,
                name = "Танк",
                role = "основная сила",
                cost = 450,
                buildTime = 35f,
                health = 1300f,
                speed = 3.2f,
                hullTurnSpeed = 110f,
                turretTurnSpeed = 90f,
                weapon = new WeaponDef
                {
                    name = "Пушка танка",
                    kind = ProjectileKind.Shell,
                    damage = 70f,
                    interval = 2.6f,
                    range = 13f,
                    projectileSpeed = 30f,
                    accuracy = 0.8f,
                    spread = 0.8f,
                    splashRadius = 1.6f,
                    craterRadius = 0.9f,
                    structureDamage = 1.6f,
                    vehicleDamage = 1.6f,
                },
                researchCost = 400,
                researchTime = 40f,
            },
            [VehicleType.Artillery] = new VehicleDef
            {
                type = VehicleType.Artillery,
                name = "Артиллерия",
                role = "навесом через стены",
                cost = 400,
                buildTime = 35f,
                health = 450f,
                speed = 2.6f,
                hullTurnSpeed = 90f,
                turretTurnSpeed = 60f,
                firesOnlyStopped = true,
                weapon = new WeaponDef
                {
                    name = "Гаубица",
                    kind = ProjectileKind.Shell,
                    indirect = true,
                    damage = 80f,
                    interval = 5.5f,
                    range = 26f,
                    minRange = 8f,
                    projectileSpeed = 16f,
                    accuracy = 0.6f,
                    spread = 2f,
                    splashRadius = 2.4f,
                    craterRadius = 1.3f,
                    structureDamage = 2.5f,
                    vehicleDamage = 1f,
                },
                researchCost = 400,
                researchTime = 45f,
            },
        };

        public static readonly IReadOnlyList<VehicleType> All = new[] { VehicleType.Scout, VehicleType.Tank, VehicleType.Artillery };

        public static VehicleDef Get(VehicleType type) => defs[type];

        public static bool NeedsResearch(VehicleType type) => defs[type].researchCost > 0;
    }
}
