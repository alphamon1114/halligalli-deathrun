using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HalliGalli.Movement
{
    public enum ReleaseCheckResult
    {
        Allowed, NetworkError, InvalidManifest, UnsupportedSchema, VersionMismatch, ProtocolMismatch
    }

    // Compiled into the player: neither local metadata nor command-line arguments authorize play.
    public static class GameRelease
    {
        public const string Version = "1.0.3";
        public const int SchemaVersion = 1;
        public const string EntryPoint = "HalliGalliMovement.exe";
        public const string BuildMetadataName = "BellRun-build.json";
        public const string LatestManifestUrl = "https://github.com/alphamon1114/halligalli-deathrun/releases/latest/download/BellRun-release.json";
        public const string ReleasesUrl = "https://github.com/alphamon1114/halligalli-deathrun/releases/latest";
        const string DownloadPrefix = "https://github.com/alphamon1114/halligalli-deathrun/releases/download/v";
        public const int MaxManifestCharacters = 16384;
        static readonly Regex StableVersion = new Regex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);
        static readonly Regex Sha256 = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

        [Serializable]
        public sealed class BuildMetadata
        {
            public int schemaVersion = SchemaVersion;
            public string version = Version;
            public int protocol = CourseNetwork.Protocol;
            public string entryPoint = EntryPoint;
        }

        // Pure response validation is shared by the live gate and the editor's focused checks.
        // Every invocation stands alone: a previous successful response is never cached authorization.
        public static ReleaseCheckResult CheckLatestResponse(bool requestSucceeded, long responseCode, string json)
        {
            if (!requestSucceeded || responseCode != 200) return ReleaseCheckResult.NetworkError;
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxManifestCharacters) return ReleaseCheckResult.InvalidManifest;
            try {
                JObject manifest;
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 4 }) {
                    manifest = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) return ReleaseCheckResult.InvalidManifest;
                }
                if (!Integer(manifest, "schemaVersion")) return ReleaseCheckResult.InvalidManifest;
                if (manifest.Value<long>("schemaVersion") != SchemaVersion) return ReleaseCheckResult.UnsupportedSchema;
                if (!Text(manifest, "version") || !Integer(manifest, "protocol") || !Text(manifest, "archiveName") ||
                    !Text(manifest, "downloadUrl") || !Text(manifest, "sha256") || !Integer(manifest, "size") ||
                    !Text(manifest, "entryPoint")) return ReleaseCheckResult.InvalidManifest;
                string version = manifest.Value<string>("version");
                string archive = "BellRun-Windows-" + version + ".zip";
                if (!StableVersion.IsMatch(version) || manifest.Value<long>("protocol") <= 0 ||
                    manifest.Value<string>("archiveName") != archive ||
                    manifest.Value<string>("downloadUrl") != DownloadPrefix + version + "/" + archive ||
                    !Sha256.IsMatch(manifest.Value<string>("sha256")) || manifest.Value<long>("size") <= 0 ||
                    manifest.Value<string>("entryPoint") != EntryPoint) return ReleaseCheckResult.InvalidManifest;
                if (!string.Equals(version, Version, StringComparison.Ordinal)) return ReleaseCheckResult.VersionMismatch;
                if (manifest.Value<long>("protocol") != CourseNetwork.Protocol) return ReleaseCheckResult.ProtocolMismatch;
                return ReleaseCheckResult.Allowed;
            } catch (JsonException) {
                return ReleaseCheckResult.InvalidManifest;
            } catch (FormatException) {
                return ReleaseCheckResult.InvalidManifest;
            } catch (OverflowException) {
                return ReleaseCheckResult.InvalidManifest;
            } catch (InvalidCastException) {
                return ReleaseCheckResult.InvalidManifest;
            } catch (ArgumentException) {
                return ReleaseCheckResult.InvalidManifest;
            }
        }

        static bool Text(JObject value, string key) => value[key]?.Type == JTokenType.String;
        static bool Integer(JObject value, string key) => value[key]?.Type == JTokenType.Integer;
    }
}
