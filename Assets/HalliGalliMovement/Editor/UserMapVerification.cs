using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class UserMapVerification
    {
        [Serializable] sealed class Check { public string name; public bool passed; }
        [Serializable] sealed class Report { public string utc; public List<Check> checks = new List<Check>(); }
        static readonly Report report = new Report();

        public static void Build()
        {
            report.utc = DateTime.UtcNow.ToString("O");
            try {
                ValidateStorage();
                FloorMarkingCleanup.Apply();
                UserMapTemplateBuilder.Build();
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                Directory.CreateDirectory("Build/Windows");
                var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = CourseMapsBuilder.BuildScenePaths(),
                    locationPathName = Path.GetFullPath("Build/Windows/HalliGalliMovement.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });
                CheckThat("Windows player with studio and user-map scene builds", build.summary.result == BuildResult.Succeeded);
                Debug.Log("USER_MAP_BUILD_VERIFIED");
            } finally {
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/usermap-storage-build.json", JsonUtility.ToJson(report, true));
            }
        }

        static void ValidateStorage()
        {
            string temp = Path.Combine(Path.GetTempPath(), "bellmap-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try {
                var map = UserMapStorage.CreateDefault();
                map.name = "공유 검사 맵";
                string original = UserMapStorage.Serialize(map);
                bool saveBlocked = false, exportBlocked = false;
                try { UserMapStorage.Save(map); } catch (InvalidOperationException) { saveBlocked = true; }
                string portable = Path.Combine(temp, "친구에게 전달.bellmap");
                try { UserMapStorage.Export(map, portable); } catch (InvalidOperationException) { exportBlocked = true; }
                CheckThat("Uncleared map cannot be saved or exported", saveBlocked && exportBlocked && !File.Exists(portable));
                // Untrusted hand-authored input must never carry a local clear proof.
                File.WriteAllText(portable, original);
                var imported = UserMapStorage.Import(portable);
                CheckThat("Import creates a new uncertified draft without saving", imported.id != map.id && !UserMapClearance.IsCleared(imported) && !File.Exists(Path.Combine(UserMapStorage.DirectoryPath, imported.id + UserMapStorage.Extension)));
                CheckThat("Portable input preserves geometry and Korean metadata", imported.name == map.name && imported.objects.Select(o => JsonUtility.ToJson(o)).SequenceEqual(map.objects.Select(o => JsonUtility.ToJson(o))));
                imported.name = "수정한 복사본";
                CheckThat("Editing imported draft does not modify original file", File.ReadAllText(portable) == original);
                var copy = UserMapStorage.Clone(map); copy.objects[0].position += Vector3.right;
                CheckThat("Draft cloning isolates nested object edits", copy.objects[0].position != map.objects[0].position);
                Reject("Unknown schema version", map, d => d.version = int.MaxValue);
                Reject("Path traversal map id", map, d => d.id = "../../outside");
                Reject("Unknown object kind", map, d => d.objects[0].kind = (UserMapPart)999);
                Reject("Nonfinite transform", map, d => d.objects[0].position.x = float.NaN);
                Reject("Duplicate object identities", map, d => d.objects[1].id = d.objects[0].id);
                Reject("No bell", map, d => d.objects = d.objects.Where(o => o.kind != UserMapPart.Bell).ToArray());
                Reject("Too many objects", map, d => d.objects = Enumerable.Repeat(d.objects[0], UserMapStorage.MaxObjects + 1).ToArray());
                string oversized = Path.Combine(temp, "oversized.bellmap");
                File.WriteAllText(oversized, new string(' ', UserMapStorage.MaxBytes + 1));
                bool rejected = false;
                try { UserMapStorage.Import(oversized); } catch (Exception) { rejected = true; }
                CheckThat("Oversized imported file rejected", rejected);
                CheckThat("Content hash changes when geometry changes", UserMapStorage.Hash(original) != UserMapStorage.Hash(UserMapStorage.Serialize(copy)));
            } finally {
                Directory.Delete(temp, true);
            }
        }

        static void Reject(string name, UserMapData map, Action<UserMapData> change)
        {
            var invalid = UserMapStorage.Clone(map); change(invalid);
            bool rejected = false;
            try { UserMapStorage.Validate(invalid); } catch (Exception) { rejected = true; }
            CheckThat(name + " rejected", rejected);
        }
        static void CheckThat(string name, bool passed)
        {
            report.checks.Add(new Check { name = name, passed = passed });
            if (!passed) throw new InvalidOperationException(name);
            Debug.Log("USER_MAP_CHECK " + name);
        }
    }
}
