using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Generals
{
    /// <summary>
    /// Сетка застройки при установке здания: квадрат клеток вокруг центра экрана.
    /// Зелёная клетка — здесь можно строить выбранное здание, красная — нельзя (занято, вне зоны, вода, стена).
    /// Меш пересобирается, когда центр сдвигается на другую клетку или меняется застройка.
    /// </summary>
    public class BuildGridOverlay : MonoBehaviour
    {
        /// <summary>Апофема квадрата сетки в клетках</summary>
        public int apothem = 50;
        /// <summary>Сколько крайних клеток плавно тают</summary>
        public int fadeCells = 8;
        /// <summary>Зазор между клетками, доля клетки</summary>
        public float gap = 0.08f;

        static readonly Color32 Free = new(60, 230, 80, 110);
        static readonly Color32 Blocked = new(240, 50, 40, 110);

        VoxelArena arena;
        BuildGrid grid;
        Faction faction;
        PlacementRule rule;

        Mesh mesh;
        MeshRenderer meshRenderer;
        readonly List<Vector3> vertices = new();
        readonly List<Color32> colors = new();
        readonly List<int> indices = new();

        int2 center;
        bool dirty;

        // Стены при установке рисуются здесь вторым проходом, полупрозрачными
        Material wallsMaterial;
        readonly List<(Mesh mesh, Matrix4x4 matrix)> wallChunks = new();

        public static BuildGridOverlay Create(VoxelArena arena)
        {
            var go = new GameObject("Сетка застройки");
            var overlay = go.AddComponent<BuildGridOverlay>();
            overlay.arena = arena;

            overlay.mesh = new Mesh { name = "BuildGrid", indexFormat = IndexFormat.UInt32 };
            overlay.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = overlay.mesh;

            overlay.meshRenderer = go.AddComponent<MeshRenderer>();
            overlay.meshRenderer.sharedMaterial = new Material(Shader.Find("NoobGenerals/BuildGrid"))
            {
                renderQueue = BuildModeVisuals.GridQueue,
            };
            overlay.wallsMaterial = BuildModeVisuals.CreateWallsMaterial(arena.Material);
            overlay.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlay.meshRenderer.receiveShadows = false;

            go.SetActive(false);
            return overlay;
        }

        public void Show(BuildGrid grid, Faction faction, PlacementRule rule, int2 center)
        {
            this.grid = grid;
            this.faction = faction;
            this.rule = rule;
            this.center = center;
            gameObject.SetActive(true);
            arena.CollectWallChunks(wallChunks);
            Rebuild();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>Центр сетки — клетка под центром экрана</summary>
        public void SetCenter(int2 cell)
        {
            if (!cell.Equals(center))
            {
                center = cell;
                dirty = true;
            }
        }

        /// <summary>Застройка изменилась (заложено здание, захвачена точка)</summary>
        public void MarkDirty() => dirty = true;

        void LateUpdate()
        {
            if (dirty)
                Rebuild();

            var rp = new RenderParams(wallsMaterial)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                layer = arena.gameObject.layer,
            };
            foreach (var (wallMesh, matrix) in wallChunks)
                Graphics.RenderMesh(rp, wallMesh, 0, matrix);
        }

        void Rebuild()
        {
            dirty = false;
            vertices.Clear();
            colors.Clear();
            indices.Clear();

            var layout = arena.Layout;
            float lift = 0.03f / arena.VoxelSize;   // чуть над землёй, в вокселях
            float fadeStart = apothem - fadeCells;

            for (int dz = -apothem; dz <= apothem; dz++)
            {
                for (int dx = -apothem; dx <= apothem; dx++)
                {
                    int x = center.x + dx, z = center.y + dz;
                    if (x < 0 || z < 0 || x >= layout.sizeX || z >= layout.sizeZ)
                        continue;

                    // Над водой клетка лежит на воде
                    float y = math.max(arena.SurfaceY(x, z), layout.waterTop[z * layout.sizeX + x]) + lift;

                    var color = grid.IsCellBuildable(faction, rule, x, z) ? Free : Blocked;
                    float edge = math.max(math.abs(dx), math.abs(dz));
                    if (edge > fadeStart)
                        color.a = (byte)(color.a * math.saturate((apothem + 1 - edge) / (fadeCells + 1)));

                    int v = vertices.Count;
                    vertices.Add(arena.VoxelToWorld(new float3(x + gap, y, z + gap)));
                    vertices.Add(arena.VoxelToWorld(new float3(x + gap, y, z + 1 - gap)));
                    vertices.Add(arena.VoxelToWorld(new float3(x + 1 - gap, y, z + 1 - gap)));
                    vertices.Add(arena.VoxelToWorld(new float3(x + 1 - gap, y, z + gap)));
                    colors.Add(color);
                    colors.Add(color);
                    colors.Add(color);
                    colors.Add(color);
                    indices.Add(v);
                    indices.Add(v + 1);
                    indices.Add(v + 2);
                    indices.Add(v);
                    indices.Add(v + 2);
                    indices.Add(v + 3);
                }
            }

            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
        }
    }
}
