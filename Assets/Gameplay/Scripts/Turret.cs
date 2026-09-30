using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Турель: поворотная башня на основании здания. Сама выбирает цель в радиусе — сначала вражеских
    /// бойцов, потом здания; стреляет, только когда ствол довёрнут и линия огня свободна.
    /// Висит на здании турели рядом со Structure.
    /// </summary>
    public class Turret : MonoBehaviour
    {
        const float ThinkInterval = 0.25f;

        public IDamageable Target { get; private set; }

        Structure structure;
        Transform head;
        Weapon weapon;
        float thinkTimer;

        public void Init(Structure structure, Transform modelRoot, Material material)
        {
            this.structure = structure;
            weapon = new Weapon(WeaponCatalog.Cannon);
            thinkTimer = Random.value * ThinkInterval;

            // Башня стоит на основании; поворачивается вокруг центра своего корпуса
            head = new GameObject("Head").transform;
            head.SetParent(modelRoot, false);
            head.localPosition = new Vector3(0f, VoxelModels.TurretBaseHeight * VoxelModels.VoxelSize, 0f);

            var model = new GameObject("Model");
            model.transform.SetParent(head, false);
            model.transform.localScale = Vector3.one * VoxelModels.VoxelSize;
            model.transform.localPosition = -VoxelModels.TurretHeadPivot * VoxelModels.VoxelSize;
            model.AddComponent<MeshFilter>().sharedMesh = VoxelModels.TurretHead(structure.Faction.team);
            model.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Башню ставят последней, когда основание достроено (Structure.Complete → SetBuilt)
            head.gameObject.SetActive(false);
        }

        /// <summary>Стройка закончена: башня встаёт на основание</summary>
        public void SetBuilt()
        {
            if (head.gameObject.activeSelf)
                return;
            head.gameObject.SetActive(true);
            // Уже стоящие с начала боя турели — без эффекта
            if (Time.timeSinceLevelLoad > 1f)
            {
                var effects = MatchManager.Instance.Effects;
                for (int i = 0; i < 6; i++)
                    effects.VoxelPlaced(head.position + Random.insideUnitSphere * 0.3f + Vector3.up * 0.3f,
                                        VoxelBlocks.SlotMetal, i % 2 == 0);
            }
        }

        // Дульный срез: конец ствола (ствол — z 3..5, высота 1..2 вокселя модели башни)
        Vector3 Muzzle => head.TransformPoint(new Vector3(0f, 1.5f, 6f - VoxelModels.TurretHeadPivot.z) * VoxelModels.VoxelSize);

        void Update()
        {
            if (!structure.IsBuilt || !structure.IsAlive)
                return;

            weapon.Tick(Time.deltaTime);

            thinkTimer -= Time.deltaTime;
            if (thinkTimer <= 0f)
            {
                thinkTimer = ThinkInterval;
                Think();
            }

            if (!Combat.IsAlive(Target))
            {
                Target = null;
                return;
            }

            var look = (Target is IAreaTarget area ? area.ClosestEdgePoint(head.position, 0f) : Target.AimPoint(head.position))
                       - head.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f)
                return;

            var desired = Quaternion.LookRotation(look);
            head.rotation = Quaternion.RotateTowards(head.rotation, desired, WeaponCatalog.TurretTurnSpeed * Time.deltaTime);

            if (weapon.Ready && Quaternion.Angle(head.rotation, desired) < WeaponCatalog.TurretAimTolerance)
                weapon.Fire(structure.Faction, Muzzle, Target);
        }

        void Think()
        {
            // Текущая цель ещё годится — не прыгаем между целями
            if (Combat.IsAlive(Target) && InRange(Target) && Combat.HasLineOfFire(Muzzle, Target, structure.Faction))
                return;

            Target = null;
            var enemy = MatchManager.Instance.GetFaction(1 - structure.Faction.team);
            var muzzle = Muzzle;

            float best = float.MaxValue;
            foreach (var unit in enemy.units)
            {
                if (!Combat.IsAlive(unit))
                    continue;
                float d = (unit.transform.position - transform.position).sqrMagnitude;
                if (d < best && InRange(unit) && Combat.HasLineOfFire(muzzle, unit, structure.Faction))
                {
                    best = d;
                    Target = unit;
                }
            }
            if (Target != null)
                return;

            foreach (var s in enemy.structures)
            {
                if (!Combat.IsAlive(s))
                    continue;
                float d = s.DistanceTo(transform.position);
                if (d < best && InRange(s) && Combat.HasLineOfFire(muzzle, s, structure.Faction))
                {
                    best = d;
                    Target = s;
                }
            }
        }

        bool InRange(IDamageable target)
        {
            float d = target is Structure s ? s.DistanceTo(transform.position) : Vector3.Distance(target.transform.position, transform.position);
            return d <= weapon.Def.range;
        }
    }
}
