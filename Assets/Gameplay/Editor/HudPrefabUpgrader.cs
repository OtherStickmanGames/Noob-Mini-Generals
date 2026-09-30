using UnityEditor;

namespace Generals.EditorTools
{
    /// <summary>После компиляции дополняет префаб HUD новыми элементами, если их в нём нет</summary>
    [InitializeOnLoad]
    static class HudPrefabUpgrader
    {
        static HudPrefabUpgrader()
        {
            EditorApplication.delayCall += HudPrefabBuilder.UpgradeIfNeeded;
        }
    }
}
