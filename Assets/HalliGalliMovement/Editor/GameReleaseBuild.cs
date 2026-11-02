using System;
using System.IO;
using System.Text;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Applies to every standalone BuildPipeline entrypoint, without rebuilding any scene or map.
    public sealed class GameReleaseBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => -10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Standalone(report)) return;
            if ((report.summary.platform == BuildTarget.StandaloneWindows || report.summary.platform == BuildTarget.StandaloneWindows64) &&
                !string.Equals(Path.GetFileName(report.summary.outputPath), GameRelease.EntryPoint, StringComparison.Ordinal))
                throw new BuildFailedException("The Windows release executable must be named " + GameRelease.EntryPoint);
            PlayerSettings.bundleVersion = GameRelease.Version;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!Standalone(report)) return;
            string directory = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
            string path = Path.Combine(directory, GameRelease.BuildMetadataName);
            File.WriteAllText(path, JsonUtility.ToJson(new GameRelease.BuildMetadata(), true), new UTF8Encoding(false));
            Debug.Log("RELEASE_BUILD_METADATA version=" + GameRelease.Version + " protocol=" + CourseNetwork.Protocol + " path=" + path);
        }

        static bool Standalone(BuildReport report) => BuildPipeline.GetBuildTargetGroup(report.summary.platform) == BuildTargetGroup.Standalone;
    }
}
