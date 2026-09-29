using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Меню Tools/Voxel Arena:
/// Create Test Setup — арена с тестовым управлением (взрывы по клику, агенты, замеры);
/// Create Match Setup — сцена боя (здания, строители, интерфейс). Запускать в пустой сцене.
/// </summary>
public static class VoxelArenaSetup
{
    const string MaterialPath = "Assets/VoxelArena/VoxelArena.mat";

    [MenuItem("Tools/Voxel Arena/Create Test Setup")]
    static void CreateTestSetup()
    {
        if (!CreateArena(out var arena))
            return;

        arena.gameObject.AddComponent<ArenaTestController>();
        EnsureCameraAndLight();
        Finish(arena.gameObject);
    }

    [MenuItem("Tools/Voxel Arena/Create Match Setup")]
    static void CreateMatchSetup()
    {
        if (!CreateArena(out var arena))
            return;

        var camera = EnsureCameraAndLight();

        var rtsCamera = camera.gameObject.AddComponent<Generals.RtsCamera>();
        SetReference(rtsCamera, "arena", arena);

        var matchObject = new GameObject("Match");
        var match = matchObject.AddComponent<Generals.MatchManager>();
        SetReference(match, "arena", arena);
        SetReference(match, "navMesh", arena.GetComponent<ArenaNavMesh>());
        Undo.RegisterCreatedObjectUndo(matchObject, "Create Match");

        // Интерфейс — экземпляр префаба (собирается при первом запуске, дальше правится руками)
        var hudPrefab = Generals.EditorTools.HudPrefabBuilder.GetOrBuild();
        var hudObject = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab);
        SetReference(hudObject.GetComponent<Generals.GameHud>(), "rtsCamera", rtsCamera);
        Undo.RegisterCreatedObjectUndo(hudObject, "Create HUD");

        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
        }

        Finish(arena.gameObject);
    }

    static bool CreateArena(out VoxelArena arena)
    {
        arena = Object.FindObjectOfType<VoxelArena>();
        if (arena != null)
        {
            Debug.Log("В сцене уже есть арена. Создайте пустую сцену и запустите меню ещё раз.", arena);
            Selection.activeGameObject = arena.gameObject;
            arena = null;
            return false;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("NoobGenerals/VoxelArena"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        var arenaObject = new GameObject("Voxel Arena");
        arena = arenaObject.AddComponent<VoxelArena>();
        SetReference(arena, "material", material);
        arenaObject.AddComponent<ArenaNavMesh>();
        Undo.RegisterCreatedObjectUndo(arenaObject, "Create Voxel Arena");
        return true;
    }

    static Camera EnsureCameraAndLight()
    {
        var camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create Camera");
        }

        if (Object.FindObjectOfType<Light>() == null)
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            Undo.RegisterCreatedObjectUndo(lightObject, "Create Light");
        }

        return camera;
    }

    static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Finish(GameObject select)
    {
        Selection.activeGameObject = select;
        EditorSceneManager.MarkSceneDirty(select.scene);
    }
}
