using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static VoxelBlocks;

namespace Generals
{
    /// <summary>
    /// Воксельные модели-заглушки зданий и юнитов. Воксель модели — 0.25 м (вдвое мельче земли),
    /// меш строится тем же GreedyMeshJob, цвета — из общей палитры, поэтому материал тот же, что у арены.
    /// Координаты меша — в вокселях модели от угла (0, 0, 0).
    /// </summary>
    public static class VoxelModels
    {
        public const float VoxelSize = 0.25f;

        static readonly Dictionary<string, Mesh> cache = new();

        public static Mesh Structure(StructureType type, int team)
        {
            return Get($"{type}_{team}", () => type switch
            {
                StructureType.Headquarters => Headquarters(team),
                StructureType.Extractor => Extractor(team),
                StructureType.Mine => Mine(team),
                StructureType.Barracks => Barracks(team),
                _ => Turret(team),
            });
        }

        /// <summary>
        /// Свежая сетка вокселей тела здания — своя у каждого здания, из неё выбиваются воксели
        /// при попаданиях. У турели — только основание (башня отдельно и поворачивается).
        /// </summary>
        public static Model StructureBody(StructureType type, int team) => type switch
        {
            StructureType.Headquarters => Headquarters(team),
            StructureType.Extractor => Extractor(team),
            StructureType.Mine => Mine(team),
            StructureType.Barracks => Barracks(team),
            _ => TurretBaseModel(team),
        };

        public static Mesh Builder(int team) => Get($"Builder_{team}", () => BuilderModel(team));

        public static Mesh Infantry(int team) => Get($"Infantry_{team}", () => InfantryModel(team));

        /// <summary>Флаг точки захвата; team = -1 — ничья</summary>
        public static Mesh Flag(int team) => Get($"Flag_{team}", () => FlagModel(team));

        /// <summary>Размер модели в вокселях — для выравнивания по центру</summary>
        public static int3 Size(Mesh mesh) => (int3)math.round((float3)mesh.bounds.size);

        static Mesh Get(string key, System.Func<Model> build)
        {
            if (cache.TryGetValue(key, out var mesh) && mesh != null)
                return mesh;

            var model = build();
            mesh = model.ToMesh(key);
            cache[key] = mesh;
            return mesh;
        }

        // ---------- Модели ----------

        static Model Headquarters(int team)
        {
            byte color = TeamColor(team), dark = TeamColorDark(team);
            var m = new Model(16, 20, 16);
            m.Box(0, 0, 0, 16, 2, 16, Stone);          // фундамент
            m.Box(1, 2, 1, 14, 8, 14, Wall);           // корпус
            // Угловые башни с верхушками цвета команды
            foreach (var (x, z) in new[] { (0, 0), (13, 0), (0, 13), (13, 13) })
            {
                m.Box(x, 2, z, 3, 10, 3, Wall);
                m.Box(x, 12, z, 3, 1, 3, color);
            }
            // Крыша ступенями
            for (int i = 0; i < 4; i++)
                m.Box(2 + i, 10 + i, 2 + i, 12 - i * 2, 1, 12 - i * 2, i % 2 == 0 ? color : dark);
            // Дверь на стороне +z (ворота модели поворачиваются к противнику)
            m.Box(6, 2, 14, 4, 5, 1, dark);
            // Флагшток и флаг
            m.Box(8, 14, 8, 1, 6, 1, Wood);
            m.Box(9, 17, 8, 4, 3, 1, color);
            return m;
        }

        // Добытчик базового ресурса: кирпичная установка с баком и трубой
        static Model Extractor(int team)
        {
            byte color = TeamColor(team), dark = TeamColorDark(team);
            var m = new Model(6, 11, 6);
            m.Box(0, 0, 0, 6, 1, 6, Stone);
            m.Box(0, 1, 0, 6, 4, 4, Wall);             // корпус
            m.Box(0, 5, 0, 6, 1, 4, color);            // крыша цвета команды
            m.Box(1, 1, 4, 4, 4, 2, Metal);            // бак
            m.Box(1, 5, 4, 4, 1, 2, dark);
            m.Box(4, 6, 1, 1, 5, 1, Metal);            // труба
            m.Box(2, 2, 3, 2, 2, 1, dark);             // окно
            return m;
        }

        // Шахта ценного ресурса: каркас с буром над рудой
        static Model Mine(int team)
        {
            byte color = TeamColor(team);
            var m = new Model(6, 10, 6);
            // Каркас из четырёх стоек над рудой
            foreach (var (x, z) in new[] { (0, 0), (5, 0), (0, 5), (5, 5) })
                m.Box(x, 0, z, 1, 7, 1, Wood);
            m.Box(0, 7, 0, 6, 1, 6, Metal);            // площадка
            m.Box(0, 8, 0, 6, 1, 1, color);            // полосы цвета команды
            m.Box(0, 8, 5, 6, 1, 1, color);
            m.Box(2, 2, 2, 2, 8, 2, Metal);            // бур
            return m;
        }

        static Model Barracks(int team)
        {
            byte color = TeamColor(team), dark = TeamColorDark(team);
            var m = new Model(12, 12, 16);
            m.Box(0, 0, 0, 12, 1, 16, Stone);
            m.Box(0, 1, 0, 12, 6, 16, Wall);
            // Двускатная крыша вдоль z
            for (int i = 0; i < 5; i++)
                m.Box(i, 7 + i, 0, 12 - i * 2, 1, 16, i % 2 == 0 ? color : dark);
            m.Box(4, 1, 15, 4, 4, 1, dark);            // ворота
            m.Box(1, 3, 15, 2, 2, 1, Metal);           // окна
            m.Box(9, 3, 15, 2, 2, 1, Metal);
            return m;
        }

        // Турель целиком (призрак при установке): основание и башня, повёрнутая стволом в +z
        static Model Turret(int team)
        {
            var m = new Model(4, 7, 4);
            TurretBaseBoxes(m, team);
            TurretHeadBoxes(m, team, 1, TurretBaseHeight, 0);
            return m;
        }

        /// <summary>Высота основания турели в вокселях модели: на нём стоит поворотная башня</summary>
        public const int TurretBaseHeight = 4;
        /// <summary>Ось поворота башни в вокселях модели башни (центр её корпуса 3×3)</summary>
        public static readonly Vector3 TurretHeadPivot = new(1.5f, 0f, 1.5f);

        public static Mesh TurretBase(int team) => Get($"TurretBase_{team}", () => TurretBaseModel(team));

        static Model TurretBaseModel(int team)
        {
            var m = new Model(4, TurretBaseHeight, 4);
            TurretBaseBoxes(m, team);
            return m;
        }

        /// <summary>Поворотная башня: корпус 3×3 и ствол в +z</summary>
        public static Mesh TurretHead(int team) => Get($"TurretHead_{team}", () =>
        {
            var m = new Model(3, 3, 6);
            TurretHeadBoxes(m, team, 0, 0, 0);
            return m;
        });

        static void TurretBaseBoxes(Model m, int team)
        {
            m.Box(0, 0, 0, 4, 3, 4, Stone);
            m.Box(0, 3, 0, 4, 1, 4, TeamColor(team));
        }

        static void TurretHeadBoxes(Model m, int team, int x, int y, int z)
        {
            m.Box(x, y, z, 3, 2, 3, Metal);                    // корпус
            m.Box(x, y + 2, z, 3, 1, 3, TeamColorDark(team));  // крыша
            m.Box(x + 1, y + 1, z + 3, 1, 1, 3, Metal);        // ствол
        }

        static Model BuilderModel(int team)
        {
            byte color = TeamColor(team), dark = TeamColorDark(team);
            var m = new Model(3, 8, 3);
            m.Box(0, 0, 1, 1, 3, 1, dark);             // ноги
            m.Box(2, 0, 1, 1, 3, 1, dark);
            m.Box(0, 3, 0, 3, 3, 3, color);            // корпус
            m.Box(1, 6, 1, 1, 1, 1, Sand);             // голова
            m.Box(0, 7, 0, 3, 1, 3, GoldOre);          // каска строителя
            return m;
        }

        // Смотрит в +z, винтовка у правого бока стволом вперёд
        static Model InfantryModel(int team)
        {
            byte color = TeamColor(team), dark = TeamColorDark(team);
            var m = new Model(3, 8, 4);
            m.Box(0, 0, 1, 1, 3, 1, dark);             // ноги
            m.Box(2, 0, 1, 1, 3, 1, dark);
            m.Box(0, 3, 0, 3, 3, 3, color);            // корпус
            m.Box(1, 6, 1, 1, 1, 1, Sand);             // голова
            m.Box(0, 7, 0, 3, 1, 3, dark);             // каска
            m.Box(2, 4, 1, 1, 1, 3, Metal);            // винтовка
            return m;
        }

        static Model FlagModel(int team)
        {
            byte color = team < 0 ? Marker : TeamColor(team);
            var m = new Model(6, 12, 1);
            m.Box(0, 0, 0, 1, 12, 1, Wood);
            m.Box(1, 8, 0, 5, 4, 1, color);
            return m;
        }

        // ---------- Сетка модели ----------

        /// <summary>Сетка вокселей модели: блоки, как у арены (0 — пусто)</summary>
        public class Model
        {
            readonly int3 size;
            readonly byte[] voxels;

            public Model(int x, int y, int z)
            {
                size = new int3(x, y, z);
                voxels = new byte[x * y * z];
            }

            Model(int3 size, byte[] voxels)
            {
                this.size = size;
                this.voxels = voxels;
            }

            public int3 Size => size;
            /// <summary>Воксели, индекс (y * size.z + z) * size.x + x</summary>
            public byte[] Voxels => voxels;

            public int Index(int x, int y, int z) => (y * size.z + z) * size.x + x;

            public int3 Coord(int index) =>
                new(index % size.x, index / (size.x * size.z), index / size.x % size.z);

            public Model Clone() => new(size, (byte[])voxels.Clone());

            public void Box(int x, int y, int z, int w, int h, int d, byte block)
            {
                for (int yy = y; yy < y + h; yy++)
                    for (int zz = z; zz < z + d; zz++)
                        for (int xx = x; xx < x + w; xx++)
                            if (xx >= 0 && yy >= 0 && zz >= 0 && xx < size.x && yy < size.y && zz < size.z)
                                voxels[(yy * size.z + zz) * size.x + xx] = block;
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                WriteMesh(mesh);
                return mesh;
            }

            /// <summary>Пересобрать меш заново в тот же объект Mesh (здание с выбитыми вокселями)</summary>
            public void WriteMesh(Mesh mesh)
            {
                int chunk = math.cmax(size);
                var data = new NativeArray<byte>(voxels, Allocator.TempJob);
                var faces = CreateFaceTable(Allocator.TempJob);
                var positions = new NativeList<float3>(256, Allocator.TempJob);
                var normals = new NativeList<float3>(256, Allocator.TempJob);
                var colors = new NativeList<Color32>(256, Allocator.TempJob);
                var indices = new NativeList<uint>(384, Allocator.TempJob);

                new GreedyMeshJob
                {
                    voxels = data,
                    faceColors = faces,
                    dims = size,
                    chunkOrigin = int3.zero,
                    chunkSize = chunk,
                    water = false,
                    positions = positions,
                    normals = normals,
                    colors = colors,
                    indices = indices,
                }
                .Schedule()
                .Complete();

                mesh.Clear();
                mesh.SetVertices(positions.AsArray());
                mesh.SetNormals(normals.AsArray());
                mesh.SetColors(colors.AsArray());
                mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0);
                mesh.bounds = new Bounds((float3)size * 0.5f, (float3)size);

                data.Dispose();
                faces.Dispose();
                positions.Dispose();
                normals.Dispose();
                colors.Dispose();
                indices.Dispose();
            }
        }
    }
}
