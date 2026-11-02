using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BellRun.Launcher.Core;

public sealed class LauncherException : Exception
{
    public LauncherException(string message) : base(message) { }
    public LauncherException(string message, Exception inner) : base(message, inner) { }
}

public sealed record ReleaseManifest(int SchemaVersion, string Version, int Protocol,
    string ArchiveName, string DownloadUrl, string Sha256, long Size, string EntryPoint)
{
    public const string Repository = "https://github.com/alphamon1114/halligalli-deathrun";
    public const string LatestUrl = Repository + "/releases/latest/download/BellRun-release.json";
    public const string GameExecutable = "HalliGalliMovement.exe";
    public const string BuildFile = "BellRun-build.json";
    public const long MaximumArchiveBytes = 4L * 1024 * 1024 * 1024;
    internal const long MaximumExpandedBytes = 12L * 1024 * 1024 * 1024;
    internal const int MaximumFiles = 100_000;
    internal const int MaximumManifestBytes = 64 * 1024;

    internal static ReleaseManifest Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireObject(root);
            int schema = root.GetProperty("schemaVersion").GetInt32();
            if (schema != 1)
                throw new LauncherException("이 배포 형식은 현재 런처에서 지원하지 않습니다. GitHub Releases에서 최신 런처를 받아 주세요.");
            var result = new ReleaseManifest(schema,
                root.GetProperty("version").GetString()!, root.GetProperty("protocol").GetInt32(),
                root.GetProperty("archiveName").GetString()!, root.GetProperty("downloadUrl").GetString()!,
                root.GetProperty("sha256").GetString()!, root.GetProperty("size").GetInt64(),
                root.GetProperty("entryPoint").GetString()!);
            result.Validate();
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new LauncherException("최신 배포 정보가 올바르지 않습니다. 실행을 차단했습니다.", ex);
        }
    }

    internal void Validate()
    {
        if (SchemaVersion != 1 || !IsVersion(Version) || Protocol < 1 ||
            ArchiveName != $"BellRun-Windows-{Version}.zip" ||
            DownloadUrl != $"{Repository}/releases/download/v{Version}/{ArchiveName}" ||
            Sha256 is null || !Regex.IsMatch(Sha256, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant) ||
            Size < 1 || Size > MaximumArchiveBytes || EntryPoint != GameExecutable)
            throw new LauncherException("배포 주소·버전·크기·해시가 안전 기준에 맞지 않습니다. 실행을 차단했습니다.");
    }

    internal static bool IsVersion(string? value)
    {
        if (value is null || value.Length > 32 ||
            !Regex.IsMatch(value, "\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z", RegexOptions.CultureInvariant))
            return false;
        return value.Split('.').All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    internal static void RequireObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new LauncherException("JSON 객체가 필요합니다.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name))
                throw new LauncherException("중복된 JSON 항목이 있습니다.");
    }

    internal void ValidateBuild(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireObject(root);
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("version").GetString() != Version ||
                root.GetProperty("protocol").GetInt32() != Protocol ||
                root.GetProperty("entryPoint").GetString() != EntryPoint)
                throw new LauncherException("게임 내부 버전·프로토콜이 배포 정보와 다릅니다.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new LauncherException("게임 내부 빌드 정보가 올바르지 않습니다.", ex);
        }
    }
}
