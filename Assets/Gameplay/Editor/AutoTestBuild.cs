using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Generals
{
    /// <summary>
    /// Сборка Windows-билда для автотеста боя (только сцена Match). Запускается из Tools/autotest.sh
    /// в копии проекта: Unity -batchmode -quit -executeMethod Generals.AutoTestBuild.Build
    /// [-autotestBuildPath путь\NMG.exe]. Билд с ключом -autotest сам проводит бой (AutoTestBootstrap).
    /// </summary>
    public static class AutoTestBuild
    {
        const string DefaultPath = "Builds/AutoTest/NMG.exe";

        [MenuItem("Tools/Voxel Arena/Build AutoTest Player")]
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autotestBuildPath");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : DefaultPath;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Match.unity" },
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[AutoTestBuild] {report.summary.result}, {report.summary.totalErrors} ошибок, {path}");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
