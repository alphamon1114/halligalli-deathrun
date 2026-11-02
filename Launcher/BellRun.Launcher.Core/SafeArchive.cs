using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BellRun.Launcher.Core;

internal sealed record InstalledFile(string Path, long Size, string Sha256);
internal sealed record InstallationIndex(ReleaseManifest Release, List<InstalledFile> Files);

internal static class SafeArchive
{
    internal const string IndexName = ".bellrun-install.json";
    internal const int MaximumIndexBytes = 32 * 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static string RelativePath(string value, bool directory = false)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 240 || value.Contains('\\') || value.StartsWith('/') ||
            Path.IsPathRooted(value) || value.Any(c => c < 32 || "<>:\"|?*".Contains(c)))
            throw new LauncherException("압축 파일에 안전하지 않은 경로가 있습니다.");
        if (directory && value.EndsWith('/')) value = value[..^1];
        string[] parts = value.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                Regex.IsMatch(part.Split('.')[0], "\\A(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new LauncherException("압축 파일에 Windows에서 안전하지 않은 경로가 있습니다.");
        }
        if (parts[0].Equals(IndexName, StringComparison.OrdinalIgnoreCase))
            throw new LauncherException("압축 파일이 런처 검증 정보를 덮어쓰려고 합니다.");
        return value;
    }

    internal static string Inside(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new LauncherException("설치 폴더 밖의 경로를 차단했습니다.");
        return full;
    }

    internal static void NoReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new LauncherException("심볼릭 링크 또는 재분석 지점이 있는 설치 경로는 사용할 수 없습니다.");
    }

    internal static void EnsureDirectory(string path)
    {
        var info = new DirectoryInfo(Path.GetFullPath(path));
        var chain = new Stack<DirectoryInfo>();
        for (DirectoryInfo? current = info; current is not null; current = current.Parent)
            chain.Push(current);
        while (chain.TryPop(out DirectoryInfo? directory))
        {
            if (!directory.Exists) directory.Create();
            NoReparse(directory.FullName);
        }
    }

    internal static async Task ExtractAsync(string archivePath, string staging, ReleaseManifest release,
        IProgress<LauncherProgress>? progress, CancellationToken cancellationToken)
    {
        EnsureDirectory(staging);
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count is < 1 or > ReleaseManifest.MaximumFiles)
            throw new LauncherException("압축 파일 항목 수가 안전 한도를 벗어났습니다.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declared = 0;
        foreach (var entry in zip.Entries)
        {
            bool directory = entry.FullName.EndsWith('/');
            string relative = RelativePath(entry.FullName, directory);
            int unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                (unixType != 0 && unixType != (directory ? 0x4000 : 0x8000)) ||
                (!directory && (entry.ExternalAttributes & (int)FileAttributes.Directory) != 0) ||
                !paths.Add(relative) || (directory && entry.Length != 0))
                throw new LauncherException("중복 경로, 링크 또는 특수 파일이 있는 압축 파일을 차단했습니다.");
            if (!directory) files.Add(relative);
            if (entry.Length < 0 || entry.Length > ReleaseManifest.MaximumExpandedBytes - declared)
                throw new LauncherException("압축 해제 크기가 안전 한도를 초과했습니다.");
            declared += entry.Length;
        }
        foreach (string relative in paths)
        {
            int slash = relative.IndexOf('/');
            while (slash >= 0)
            {
                if (files.Contains(relative[..slash]))
                    throw new LauncherException("파일과 폴더 경로가 충돌하는 압축 파일입니다.");
                slash = relative.IndexOf('/', slash + 1);
            }
        }

        var index = new InstallationIndex(release, new List<InstalledFile>(files.Count));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long total = 0;
        long lastReport = 0;
        try
        {
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool directory = entry.FullName.EndsWith('/');
                string relative = RelativePath(entry.FullName, directory);
                string destination = Inside(staging, relative);
                if (directory) { EnsureDirectory(destination); continue; }
                EnsureDirectory(Path.GetDirectoryName(destination)!);
                await using var input = entry.Open();
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long fileSize = 0;
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(0, 128 * 1024), cancellationToken).ConfigureAwait(false)) != 0)
                {
                    fileSize += count;
                    if (fileSize > entry.Length || total > ReleaseManifest.MaximumExpandedBytes - count)
                        throw new LauncherException("압축 파일이 선언된 크기를 초과했습니다.");
                    total += count;
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (Environment.TickCount64 - lastReport >= 100)
                    {
                        progress?.Report(new LauncherProgress("게임 파일 압축 해제·검증 중", total, declared));
                        lastReport = Environment.TickCount64;
                    }
                }
                if (fileSize != entry.Length)
                    throw new LauncherException("압축 파일이 손상되었습니다.");
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
                index.Files.Add(new InstalledFile(relative, fileSize, Convert.ToHexStringLower(hash.GetHashAndReset())));
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        if (!index.Files.Any(file => file.Path == release.EntryPoint && file.Size > 0) ||
            !index.Files.Any(file => file.Path == ReleaseManifest.BuildFile))
            throw new LauncherException("게임 실행 파일 또는 빌드 정보가 없습니다.");
        string buildPath = Inside(staging, ReleaseManifest.BuildFile);
        if (new FileInfo(buildPath).Length > ReleaseManifest.MaximumManifestBytes)
            throw new LauncherException("게임 빌드 정보가 너무 큽니다.");
        release.ValidateBuild(await File.ReadAllBytesAsync(buildPath, cancellationToken).ConfigureAwait(false));
        await using var indexOutput = new FileStream(Path.Combine(staging, IndexName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(indexOutput, index, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (indexOutput.Length > MaximumIndexBytes)
            throw new LauncherException("설치 검증 정보가 너무 큽니다.");
        await indexOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
        indexOutput.Flush(true);
    }

    internal static void DeleteOwnedTree(string path)
    {
        if (!Directory.Exists(path)) return;
        NoReparse(path);
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry);
                else File.Delete(entry);
            }
            else if ((attributes & FileAttributes.Directory) != 0) DeleteOwnedTree(entry);
            else File.Delete(entry);
        }
        Directory.Delete(path);
    }
}
