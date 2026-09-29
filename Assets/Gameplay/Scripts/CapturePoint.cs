using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Generals
{
    /// <summary>Тот, кто может захватывать точки (боевые юниты)</summary>
    public interface ICapturer
    {
        int Team { get; }
        Vector3 Position { get; }
        bool IsAlive { get; }
    }

    /// <summary>
    /// Точка захвата с ценным ресурсом. Захватывает сторона, чьи юниты стоят в радиусе одни.
    /// Пока точка своя — капает ценный ресурс; добытчик на ней даёт больше.
    /// </summary>
    public class CapturePoint : MonoBehaviour
    {
        public const float Radius = 4f;
        public const float CaptureTime = 10f;

        public static readonly List<ICapturer> Capturers = new();

        public int2 Cell { get; private set; }
        public int Owner { get; private set; } = -1;
        /// <summary>Прогресс захвата 0..1 стороной CapturingTeam</summary>
        public float Progress { get; private set; }
        public int CapturingTeam { get; private set; } = -1;
        public Structure Extractor { get; set; }

        MeshFilter flag;

        public void Init(int2 cell, Vector3 position, Material material)
        {
            Cell = cell;
            name = $"Точка захвата {cell.x}_{cell.y}";
            transform.position = position;

            var model = new GameObject("Flag");
            model.transform.SetParent(transform, false);
            model.transform.localScale = Vector3.one * VoxelModels.VoxelSize;
            // Флаг на краю кольца, чтобы не стоять в рудном бугре
            model.transform.localPosition = new Vector3(1.4f, 0f, 1.4f);
            flag = model.AddComponent<MeshFilter>();
            model.AddComponent<MeshRenderer>().sharedMaterial = material;
            UpdateFlag();
        }

        public void SetOwner(int team)
        {
            Owner = team;
            Progress = 0f;
            CapturingTeam = -1;
            UpdateFlag();
        }

        void UpdateFlag()
        {
            flag.sharedMesh = VoxelModels.Flag(Owner);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (Owner >= 0)
                MatchManager.Instance.GetFaction(Owner).valuable += StructureCatalog.CapturePointIncome * dt;

            // Кто стоит в радиусе
            bool one = false, two = false;
            float radiusSq = Radius * Radius;
            foreach (var c in Capturers)
            {
                if (!c.IsAlive)
                    continue;
                var d = c.Position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > radiusSq)
                    continue;
                if (c.Team == 0) one = true;
                else two = true;
            }

            int present = one && !two ? 0 : two && !one ? 1 : -1;
            if (present < 0 || present == Owner)
            {
                // Никого, спор или свои — прогресс чужого захвата спадает
                Progress = math.max(0f, Progress - dt / CaptureTime * 0.5f);
                return;
            }

            if (CapturingTeam != present)
            {
                CapturingTeam = present;
                Progress = 0f;
            }

            Progress += dt / CaptureTime;
            if (Progress >= 1f)
                SetOwner(present);
        }
    }
}
