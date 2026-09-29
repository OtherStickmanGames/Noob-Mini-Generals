using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Меню Tools/Voxel Arena/Create Test Setup: создаёт в открытой сцене арену,
/// материал, камеру и свет. Запускать в пустой сцене.
/// </summary>
public static class VoxelArenaSetup
{
    const string MaterialPath = "Assets/VoxelArena/VoxelArena.mat";

    [MenuItem("Tools/Voxel Arena/Create Test Setup")]
    static void CreateTestSetup()
    {
        var existing = Object.FindObjectOfType<VoxelArena>();
        if (existing != null)
        {
            Debug.Log("В сцене уже есть арена, вторая не создаётся", existing);
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("NoobGenerals/VoxelArena"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        var arenaObject = new GameObject("Voxel Arena");
        var arena = arenaObject.AddComponent<VoxelArena>();

        var serialized = new SerializedObject(arena);
        serialized.FindProperty("material").objectReferenceValue = material;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        arenaObject.AddComponent<ArenaNavMesh>();
        arenaObject.AddComponent<ArenaTestController>();
        Undo.RegisterCreatedObjectUndo(arenaObject, "Create Voxel Arena");

        if (Camera.main == null)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.AddComponent<Camera>();
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

        Selection.activeGameObject = arenaObject;
        EditorSceneManager.MarkSceneDirty(arenaObject.scene);
    }
}
