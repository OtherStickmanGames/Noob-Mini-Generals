using System;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Запуск автотеста в билде: NMG.exe -autotest [-autotestSeconds 600] [-autotestScale 3]
    /// [-autotestOut путь]. Без ключа -autotest ничего не делает.
    /// </summary>
    public static class AutoTestBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-autotest") < 0)
                return;

            var go = new GameObject("AutoTest");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var driver = go.AddComponent<AutoTestDriver>();
            driver.durationSeconds = Float(args, "-autotestSeconds", driver.durationSeconds);
            driver.timeScale = Float(args, "-autotestScale", driver.timeScale);
            driver.outputDir = Arg(args, "-autotestOut") ?? driver.outputDir;
            driver.bonusIncome = Float(args, "-autotestBonus", 0f);
            driver.peaceSeconds = Float(args, "-autotestPeace", 0f);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static float Float(string[] args, string name, float fallback) =>
            float.TryParse(Arg(args, name), System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    }
}
