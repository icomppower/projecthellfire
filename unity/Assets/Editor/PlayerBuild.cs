using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hellfire.EditorTools
{
    /// <summary>
    /// Native macOS player build — the GDD's accepted distribution cost: a
    /// downloadable app, not a URL. CLI:
    /// Unity -batchmode -quit -projectPath unity -executeMethod Hellfire.EditorTools.PlayerBuild.BuildMac
    /// Run SceneBootstrap.Build first; the scene is generated, not committed.
    /// </summary>
    public static class PlayerBuild
    {
        public static void BuildMac()
        {
            try
            {
                var report = BuildPipeline.BuildPlayer(
                    new[] { "Assets/Scenes/Main.unity" },
                    "../builds/ProjectHellfire.app",
                    BuildTarget.StandaloneOSX,
                    BuildOptions.None);
                bool ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log($"[PlayerBuild] {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, " +
                          $"{report.summary.totalErrors} errors");
                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerBuild] FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }
    }
}
