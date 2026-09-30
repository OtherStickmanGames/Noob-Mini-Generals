using System.Collections.Generic;

namespace Generals
{
    public enum ProjectileKind
    {
        /// <summary>Пуля: тонкий трассер, попадание — урон одной цели</summary>
        Bullet,
        /// <summary>Снаряд: взрывается, урон по площади</summary>
        Shell,
        /// <summary>Струя огня: короткая дальность, урон по площади, без воронки</summary>
        Flame,
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
        /// <summary>Множитель урона по зданиям и стенам (у каждого оружия своя роль)</summary>
        public float structureDamage = 1f;
    }

    /// <summary>Вооружение отряда: винтовки у всех или спецоружие у SpecialsPerSquad бойцов</summary>
    public enum SquadWeapon
    {
        Rifle,
        MachineGun,
        GrenadeLauncher,
        Flamethrower,
        Sniper,
    }

    /// <summary>Спецоружие: исследуется в оружейной, выбирается в казармах</summary>
    public class SpecialWeapon
    {
        public SquadWeapon id;
        public string name;
        /// <summary>Коротко, против чего — в оружейной и казармах</summary>
        public string role;
        public WeaponDef def;
        /// <summary>Надбавка к цене отряда с этим оружием</summary>
        public int squadSurcharge;
        public int researchCost;
        public float researchTime;
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

        // ---------- Спецоружие отрядов (оружейная → казармы) ----------

        /// <summary>Сколько бойцов отряда несут спецоружие (как в DoW: остальные — с винтовками)</summary>
        public const int SpecialsPerSquad = 2;
        /// <summary>Сколько спецоружий открыто одновременно; изучил третье — одно из прежних забыто</summary>
        public const int MaxKnownSpecials = 2;

        public static readonly SpecialWeapon MachineGun = new()
        {
            id = SquadWeapon.MachineGun,
            name = "Пулемёт",
            role = "против пехоты",
            squadSurcharge = 50,
            researchCost = 200,
            researchTime = 30f,
            def = new WeaponDef
            {
                name = "Пулемёт",
                kind = ProjectileKind.Bullet,
                damage = 4f,
                interval = 0.22f,
                range = 7f,
                projectileSpeed = 40f,
                accuracy = 0.5f,
                spread = 3f,
                craterRadius = 0.3f,
                structureDamage = 0.4f,
            },
        };

        public static readonly SpecialWeapon GrenadeLauncher = new()
        {
            id = SquadWeapon.GrenadeLauncher,
            name = "Гранатомёт",
            role = "против зданий и стен",
            squadSurcharge = 75,
            researchCost = 250,
            researchTime = 35f,
            def = new WeaponDef
            {
                name = "Гранатомёт",
                kind = ProjectileKind.Shell,
                damage = 28f,
                interval = 3f,
                range = 10f,
                projectileSpeed = 18f,
                accuracy = 0.75f,
                spread = 1.5f,
                splashRadius = 1.5f,
                craterRadius = 0.8f,
                structureDamage = 2.2f,
            },
        };

        public static readonly SpecialWeapon Flamethrower = new()
        {
            id = SquadWeapon.Flamethrower,
            name = "Огнемёт",
            role = "вблизи против скоплений",
            squadSurcharge = 60,
            researchCost = 200,
            researchTime = 30f,
            def = new WeaponDef
            {
                name = "Огнемёт",
                kind = ProjectileKind.Flame,
                damage = 7f,
                interval = 0.25f,
                range = 4.5f,
                projectileSpeed = 14f,
                accuracy = 0.9f,
                spread = 5f,
                splashRadius = 1.2f,
                structureDamage = 0.5f,
            },
        };

        public static readonly SpecialWeapon Sniper = new()
        {
            id = SquadWeapon.Sniper,
            name = "Снайперская винтовка",
            role = "издалека по бойцам",
            squadSurcharge = 60,
            researchCost = 220,
            researchTime = 30f,
            def = new WeaponDef
            {
                name = "Снайперская винтовка",
                kind = ProjectileKind.Bullet,
                damage = 45f,
                interval = 3.2f,
                range = 14f,
                projectileSpeed = 70f,
                accuracy = 0.9f,
                spread = 0.3f,
                craterRadius = 0.35f,
                structureDamage = 0.3f,
            },
        };

        public static readonly IReadOnlyList<SpecialWeapon> Specials = new[] { MachineGun, GrenadeLauncher, Flamethrower, Sniper };

        public static SpecialWeapon Special(SquadWeapon id)
        {
            foreach (var s in Specials)
                if (s.id == id)
                    return s;
            return null;
        }

        /// <summary>Оружие бойца: винтовка или спецоружие</summary>
        public static WeaponDef Def(SquadWeapon id) => id == SquadWeapon.Rifle ? Rifle : Special(id).def;

        public static string Name(SquadWeapon id) => id == SquadWeapon.Rifle ? "Винтовки" : Special(id).name;

        /// <summary>Цена отряда с этим вооружением</summary>
        public static int SquadCost(SquadWeapon id) =>
            UnitCatalog.SquadCost + (id == SquadWeapon.Rifle ? 0 : Special(id).squadSurcharge);

        /// <summary>Цена бойца пополнения: спецбоец дороже на свою долю надбавки отряда</summary>
        public static int ReinforceCost(SquadWeapon trooperWeapon) =>
            UnitCatalog.ReinforceCost + (trooperWeapon == SquadWeapon.Rifle ? 0 : Special(trooperWeapon).squadSurcharge / SpecialsPerSquad);

        /// <summary>Скорость поворота башни турели, градусы в секунду</summary>
        public const float TurretTurnSpeed = 200f;
        /// <summary>Турель стреляет, когда ствол смотрит на цель точнее этого, градусы</summary>
        public const float TurretAimTolerance = 6f;
    }
}
