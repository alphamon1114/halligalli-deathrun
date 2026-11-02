using System;
using HalliGalli.Movement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Pure contract checks only: no network, build, scene changes, or player preferences.
    public static class GameReleaseVerification
    {
        [MenuItem("HalliGalli/Release/Validate Admission Contract")]
        public static void Run()
        {
            JObject valid = Manifest();
            string json = valid.ToString(Formatting.None);
            Expect("matching live response", ReleaseCheckResult.Allowed, true, 200, json);
            Expect("offline", ReleaseCheckResult.NetworkError, false, 0, null);
            Expect("transport failure cannot reuse a matching body", ReleaseCheckResult.NetworkError, false, 200, json);
            Expect("missing release", ReleaseCheckResult.NetworkError, false, 404, "Not Found");
            Expect("non-200 response", ReleaseCheckResult.NetworkError, true, 304, json);
            Expect("retry with a new matching response", ReleaseCheckResult.Allowed, true, 200, json);
            Expect("previous success never authorizes a failed response", ReleaseCheckResult.NetworkError, false, 0, json);
            Expect("empty body", ReleaseCheckResult.InvalidManifest, true, 200, "");
            Expect("malformed JSON", ReleaseCheckResult.InvalidManifest, true, 200, "{");
            Expect("wrong JSON root", ReleaseCheckResult.InvalidManifest, true, 200, "[]");
            Expect("trailing JSON", ReleaseCheckResult.InvalidManifest, true, 200, json + "{}");
            Expect("duplicate field", ReleaseCheckResult.InvalidManifest, true, 200, "{\"schemaVersion\":1," + json.Substring(1));
            Expect("oversized body", ReleaseCheckResult.InvalidManifest, true, 200, new string(' ', GameRelease.MaxManifestCharacters + 1));
            Change("unsupported schema", m => m["schemaVersion"] = 2, ReleaseCheckResult.UnsupportedSchema);
            Change("string schema", m => m["schemaVersion"] = "1");
            Change("missing protocol", m => m.Remove("protocol"));
            Change("fractional protocol", m => m["protocol"] = 35.5);
            Change("protocol mismatch", m => m["protocol"] = CourseNetwork.Protocol + 1, ReleaseCheckResult.ProtocolMismatch);
            var currentVersion = new Version(GameRelease.Version);
            string newerVersion = new Version(currentVersion.Major, currentVersion.Minor, checked(currentVersion.Build + 1)).ToString();
            Change("newer release", m => SetVersion(m, newerVersion), ReleaseCheckResult.VersionMismatch);
            if (currentVersion != new Version(0, 0, 0))
                Change("older release", m => SetVersion(m, "0.0.0"), ReleaseCheckResult.VersionMismatch);
            Change("version mismatch takes precedence over protocol mismatch", m => {
                SetVersion(m, newerVersion);
                m["protocol"] = CourseNetwork.Protocol + 1;
            }, ReleaseCheckResult.VersionMismatch);
            Change("prerelease version", m => SetVersion(m, "1.0.0-preview"));
            Change("noncanonical numeric version", m => SetVersion(m, "01.0.0"));
            Change("missing checksum", m => m.Remove("sha256"));
            Change("invalid checksum", m => m["sha256"] = new string('z', 64));
            Change("uppercase checksum", m => m["sha256"] = new string('A', 64));
            Change("empty archive", m => m["size"] = 0);
            Change("string archive size", m => m["size"] = "1024");
            Change("overflowed archive size", m => m["size"] = new JRaw("9999999999999999999999999999999999999999"));
            Change("unexpected download host", m => m["downloadUrl"] = "https://example.com/game.zip");
            Change("unexpected archive name", m => m["archiveName"] = "../game.zip");
            Change("unexpected executable", m => m["entryPoint"] = "../Other.exe");
            Debug.Log("RELEASE_GATE_CONTRACT_VERIFIED version=" + GameRelease.Version + " protocol=" + CourseNetwork.Protocol);
        }

        static JObject Manifest()
        {
            var value = new JObject {
                ["schemaVersion"] = GameRelease.SchemaVersion,
                ["protocol"] = CourseNetwork.Protocol,
                ["sha256"] = new string('a', 64),
                ["size"] = 1024,
                ["entryPoint"] = GameRelease.EntryPoint
            };
            SetVersion(value, GameRelease.Version);
            return value;
        }

        static void SetVersion(JObject manifest, string version)
        {
            string archive = "BellRun-Windows-" + version + ".zip";
            manifest["version"] = version;
            manifest["archiveName"] = archive;
            manifest["downloadUrl"] = "https://github.com/alphamon1114/halligalli-deathrun/releases/download/v" + version + "/" + archive;
        }

        static void Change(string name, Action<JObject> change, ReleaseCheckResult expected = ReleaseCheckResult.InvalidManifest)
        {
            var manifest = Manifest();
            change(manifest);
            Expect(name, expected, true, 200, manifest.ToString(Formatting.None));
        }

        static void Expect(string name, ReleaseCheckResult expected, bool succeeded, long code, string json)
        {
            var actual = GameRelease.CheckLatestResponse(succeeded, code, json);
            if (actual != expected) throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual);
            Debug.Log("RELEASE_GATE_CONTRACT_CHECK " + name + " result=" + actual);
        }
    }
}
