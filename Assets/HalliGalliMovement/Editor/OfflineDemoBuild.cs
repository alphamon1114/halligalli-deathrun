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
    public static class OfflineDemoBuild
    {
        public const string ScriptingDefine = "BELL_RUN_OFFLINE_DEMO";
        public const string EntryPoint = "HalliGalliRunDemo.exe";
        public const string MetadataName = "HalliGalliRunDemo-build.json";
        internal static bool IsBuilding { get; private set; }

        [Serializable]
        sealed class Metadata
        {
            public int schemaVersion = 1;
            public string edition = "offline-demo";
            public string version = GameRelease.Version;
            public string entryPoint = EntryPoint;
            public bool requiresInternet = false;
            public bool onlineMultiplayer = false;
        }

        [MenuItem("HalliGalli/Release/Build Offline Exhibition Demo")]
        public static void Build()
        {
            if (IsBuilding) throw new InvalidOperationException("An offline demo build is already running.");
            var target = NamedBuildTarget.Standalone;
            if (HasPersistentDemoDefine())
                throw new BuildFailedException("Remove " + ScriptingDefine + " from project scripting defines. The demo uses build-local defines only.");
            string productName = PlayerSettings.productName;
            string version = PlayerSettings.bundleVersion;
            var backend = PlayerSettings.GetScriptingBackend(target);
            try {
                IsBuilding = true;
                PlayerSettings.productName = "HalliGalli Run Offline Demo";
                PlayerSettings.bundleVersion = GameRelease.Version;
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                Directory.CreateDirectory("Build/OfflineDemo");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = CourseMapsBuilder.BuildScenePaths(),
                    locationPathName = Path.GetFullPath("Build/OfflineDemo/" + EntryPoint),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                    extraScriptingDefines = new[] { ScriptingDefine }
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Offline demo build failed: " + report.summary.result);
                Debug.Log("OFFLINE_DEMO_BUILT version=" + GameRelease.Version + " output=" + report.summary.outputPath);
            } finally {
                PlayerSettings.productName = productName;
                PlayerSettings.bundleVersion = version;
                PlayerSettings.SetScriptingBackend(target, backend);
                IsBuilding = false;
                AssetDatabase.SaveAssets();
            }
        }

        internal static bool HasPersistentDemoDefine()
        {
            foreach (string define in PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';'))
                if (string.Equals(define.Trim(), ScriptingDefine, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static void WriteMetadata(string directory)
        {
            if (File.Exists(Path.Combine(directory, GameRelease.BuildMetadataName)))
                throw new BuildFailedException("Offline output must not contain public release metadata.");
            string path = Path.Combine(directory, MetadataName);
            File.WriteAllText(path, JsonUtility.ToJson(new Metadata(), true), new UTF8Encoding(false));
            Debug.Log("OFFLINE_DEMO_BUILD_METADATA path=" + path);
        }
    }
}
