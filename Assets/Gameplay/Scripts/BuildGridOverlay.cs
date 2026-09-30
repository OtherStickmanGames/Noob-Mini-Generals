using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Generals
{
    /// <summary>
    /// Сетка застройки при установке здания: квадрат клеток вокруг центра экрана.
    /// Зелёная клетка — здесь можно строить выбранное здание, красная — нельзя (занято, вне зоны, вода, стена).
    /// Клетки лежат вплотную; яркие линии сетки и бледную заливку рисует шейдер BuildGrid по UV клетки.
    /// Вид (цвета, линии, размер) — BuildGridStyle из инспектора HUD, применяется через ApplyStyle.
    /// Меш пересобирается, когда центр сдвигается на другую клетку или меняется застройка.
    /// </summary>
    public class BuildGridOverlay : MonoBehaviour
    {
        /// <summary>Апофема квадрата сетки в клетках</summary>
        public int Apothem => apothem;

        int apothem = 50;
        int fadeCells = 8;

        // Цвет вершины: r — клетка занята (1) или свободна (0), a — затухание к краю сетки.
        // Сами цвета, линии и заливка — свойства материала
        static readonly Color32 Free = new(0, 0, 0, 255);
        static readonly Color32 Blocked = new(255, 0, 0, 255);

        static readonly int FreeColorId = Shader.PropertyToID("_FreeColor");
        static readonly int BlockedColorId = Shader.PropertyToID("_BlockedColor");
        static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        static readonly int LineAlphaId = Shader.PropertyToID("_LineAlpha");
        static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");

        VoxelArena arena;
        BuildGrid grid;
        Faction faction;
        PlacementRule rule;

        Mesh mesh;
        MeshRenderer meshRenderer;
        readonly List<Vector3> vertices = new();
        readonly List<Color32> colors = new();
        readonly List<Vector2> uvs = new();
        readonly List<int> indices = new();

        int2 center;
        bool dirty;

        // Стены при установке рисуются здесь вторым проходом, полупрозрачными
        Material wallsMaterial;

        /// <summary>Материал второго прохода стен: его прозрачность и приглушение задаёт GameHud</summary>
        public Material WallsMaterial => wallsMaterial;
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

        /// <summary>Применить вид: цвета и линии — сразу через материал, размер — пересборкой меша</summary>
        public void ApplyStyle(BuildGridStyle style)
        {
            var material = meshRenderer.sharedMaterial;
            material.SetColor(FreeColorId, style.freeColor);
            material.SetColor(BlockedColorId, style.blockedColor);
            material.SetFloat(LineWidthId, style.lineWidth);
            material.SetFloat(LineAlphaId, style.lineAlpha);
            material.SetFloat(FillAlphaId, style.fillAlpha);

            if (style.apothem != apothem || style.fadeCells != fadeCells)
            {
                apothem = style.apothem;
                fadeCells = style.fadeCells;
                dirty = true;
            }
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
            uvs.Clear();
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
                    vertices.Add(arena.VoxelToWorld(new float3(x, y, z)));
                    vertices.Add(arena.VoxelToWorld(new float3(x, y, z + 1)));
                    vertices.Add(arena.VoxelToWorld(new float3(x + 1, y, z + 1)));
                    vertices.Add(arena.VoxelToWorld(new float3(x + 1, y, z)));
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(0f, 1f));
                    uvs.Add(new Vector2(1f, 1f));
                    uvs.Add(new Vector2(1f, 0f));
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
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
        }
    }
}
