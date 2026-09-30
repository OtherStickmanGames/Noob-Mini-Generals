using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Generals.EditorTools
{
    /// <summary>
    /// Добавляет OutlineFeature во все ассеты рендерера URP проекта, где её ещё нет.
    /// Запускается сам после компиляции скриптов, руками ничего настраивать не нужно.
    /// Меню на всякий случай: Tools/Voxel Arena/Install Outline Feature.
    /// </summary>
    [InitializeOnLoad]
    static class OutlineFeatureInstaller
    {
        static OutlineFeatureInstaller()
        {
            EditorApplication.delayCall += Install;
        }

        [MenuItem("Tools/Voxel Arena/Install Outline Feature")]
        static void Install()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null || HasFeature(data))
                    continue;

                var feature = ScriptableObject.CreateInstance<OutlineFeature>();
                feature.name = "Selection Outline";
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

                // Так же, как добавляет фичу инспектор рендерера URP: список фич и карта их локальных id
                var so = new SerializedObject(data);
                var features = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;

                // Контуру нужна промежуточная текстура камеры (маска рисуется с её буфером глубины)
                var intermediate = so.FindProperty("m_IntermediateTextureMode");
                if (intermediate != null)
                    intermediate.enumValueIndex = (int)IntermediateTextureMode.Always;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                Debug.Log($"[Outline] Фича контура добавлена в {path}");
            }
            AssetDatabase.SaveAssets();
        }

        static bool HasFeature(UniversalRendererData data)
        {
            foreach (var f in data.rendererFeatures)
                if (f is OutlineFeature)
                    return true;
            return false;
        }
    }
}
