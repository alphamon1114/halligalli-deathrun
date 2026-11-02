using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BellRun.Launcher.Core;

public sealed record LauncherState(ReleaseManifest Latest, string? InstalledVersion, bool IsReady, bool GameRunning);

public sealed class LauncherEngine : IDisposable
{
    public static string ProductionRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BellRunLauncher");
    private readonly string root;
    private readonly HttpClient client;
    private readonly ReleaseTransport transport;
    private readonly Func<bool> gameIsRunning;
    private readonly Action<ProcessStartInfo> startGame;
    private readonly FileStream instanceLock;
    private readonly SemaphoreSlim operation = new(1, 1);
    private bool disposed;

    public LauncherEngine() : this(ProductionRoot, ReleaseTransport.CreateClient(), IsGameRunning, StartGame) { }

    internal LauncherEngine(string installationRoot, HttpClient httpClient, Func<bool> gameIsRunning,
        Action<ProcessStartInfo> startGame)
    {
        root = Path.GetFullPath(installationRoot);
        client = httpClient;
        transport = new ReleaseTransport(client);
        this.gameIsRunning = gameIsRunning;
        this.startGame = startGame;
        SafeArchive.EnsureDirectory(root);
        string lockPath = Path.Combine(root, "launcher.lock");
        if (File.Exists(lockPath)) SafeArchive.NoReparse(lockPath);
        try
        {
            instanceLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            client.Dispose();
            throw new LauncherException("이미 실행 중인 BellRun 런처가 있습니다. 기존 창을 사용해 주세요.", ex);
        }
    }

    public async Task<LauncherState> CheckAsync(IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            progress?.Report(new LauncherProgress("GitHub에서 최신 버전 확인 중"));
            ReleaseManifest latest = await transport.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            string? installed = ReadActiveDirectory();
            bool valid = await VerifyInstallationAsync(installed, latest, progress, cancellationToken).ConfigureAwait(false);
            return new LauncherState(latest, ReadInstalledVersion(installed), valid, gameIsRunning());
        }
        finally { operation.Release(); }
    }

    public async Task<LauncherState> InstallLatestAsync(IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureGameStopped();
            progress?.Report(new LauncherProgress("GitHub에서 최신 버전 확인 중"));
            ReleaseManifest latest = await transport.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            string? installed = ReadActiveDirectory();
            if (!await VerifyInstallationAsync(installed, latest, progress, cancellationToken).ConfigureAwait(false))
                installed = await InstallAsync(latest, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new LauncherProgress("설치 후 최신 배포 정보 다시 확인 중"));
            ReleaseManifest confirmed = await transport.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            if (confirmed != latest)
                throw new LauncherException("작업 중 새 버전이 배포되었습니다. 최신 확인 / 재시도로 다시 업데이트해 주세요. 실행하지 않았습니다.");
            return new LauncherState(confirmed, ReadInstalledVersion(installed), true, gameIsRunning());
        }
        finally { operation.Release(); }
    }

    public async Task PlayAsync(IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureGameStopped();
            progress?.Report(new LauncherProgress("실행 전 최신 버전 새로 확인 중"));
            ReleaseManifest latest = await transport.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            string? installed = ReadActiveDirectory();
            if (!await VerifyInstallationAsync(installed, latest, progress, cancellationToken).ConfigureAwait(false))
            {
                progress?.Report(new LauncherProgress("최신 게임 설치 또는 손상 파일 복구 중"));
                installed = await InstallAsync(latest, progress, cancellationToken).ConfigureAwait(false);
            }
            progress?.Report(new LauncherProgress("실행 직전 최신 배포 정보 최종 확인 중"));
            ReleaseManifest confirmed = await transport.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
            if (confirmed != latest)
                throw new LauncherException("확인 중 최신 버전이 변경되었습니다. 업데이트 후 다시 실행해 주세요. 이전 버전은 실행하지 않습니다.");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureGameStopped();
            var start = new ProcessStartInfo(Path.Combine(installed!, latest.EntryPoint))
            {
                WorkingDirectory = installed!,
                UseShellExecute = false
            };
            startGame(start);
        }
        finally { operation.Release(); }
    }

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!await operation.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new LauncherException("다른 확인 또는 업데이트 작업이 진행 중입니다.");
    }

    private void EnsureGameStopped()
    {
        if (gameIsRunning())
            throw new LauncherException("게임이 실행 중입니다. 게임을 완전히 종료한 뒤 업데이트하거나 다시 실행해 주세요.");
    }

    private async Task<string> InstallAsync(ReleaseManifest release, IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureGameStopped();
        SafeArchive.EnsureDirectory(root);
        string work = Path.Combine(root, "work-" + Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(work, "game");
        string versions = Path.Combine(root, "versions");
        string directoryName = $"v{release.Version}-{Guid.NewGuid():N}";
        string destination = Path.Combine(versions, directoryName);
        string pointerTemp = Path.Combine(work, "active-install.tmp");
        bool promoted = false;
        bool committed = false;
        SafeArchive.EnsureDirectory(work);
        try
        {
            string archive = Path.Combine(work, "download.zip");
            await transport.DownloadAsync(release, archive, progress, cancellationToken).ConfigureAwait(false);
            await SafeArchive.ExtractAsync(archive, staging, release, progress, cancellationToken).ConfigureAwait(false);
            if (!await VerifyInstallationAsync(staging, release, progress, cancellationToken).ConfigureAwait(false))
                throw new LauncherException("압축 해제 후 설치 무결성 확인에 실패했습니다. 기존 게임은 유지됩니다.");
            await File.WriteAllTextAsync(pointerTemp, directoryName, cancellationToken).ConfigureAwait(false);
            using (var pointer = new FileStream(pointerTemp, FileMode.Open, FileAccess.Write, FileShare.None)) pointer.Flush(true);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureGameStopped();
            SafeArchive.EnsureDirectory(versions);
            Directory.Move(staging, destination);
            promoted = true;
            string active = Path.Combine(root, "active-install.txt");
            if (File.Exists(active)) SafeArchive.NoReparse(active);
            File.Move(pointerTemp, active, true);
            committed = true;
            progress?.Report(new LauncherProgress("설치 완료 — 기존 설치와 사용자 데이터는 보존됩니다."));
            return destination;
        }
        finally
        {
            // Only directories created by this transaction may be removed. Old versions and Unity's LocalLow data are never touched.
            TryDeleteOwned(work);
            if (promoted && !committed) TryDeleteOwned(destination);
        }
    }

    private string? ReadActiveDirectory()
    {
        string pointer = Path.Combine(root, "active-install.txt");
        if (!File.Exists(pointer)) return null;
        try
        {
            SafeArchive.NoReparse(pointer);
            if (new FileInfo(pointer).Length > 128) return null;
            string name = File.ReadAllText(pointer);
            if (!Regex.IsMatch(name, "\\Av(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)-[0-9a-f]{32}\\z", RegexOptions.CultureInvariant)) return null;
            string versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions)) return null;
            SafeArchive.NoReparse(versions);
            string directory = Path.Combine(versions, name);
            if (!Directory.Exists(directory)) return null;
            SafeArchive.NoReparse(directory);
            return directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LauncherException) { return null; }
    }

    private static InstallationIndex? ReadIndex(string? directory)
    {
        if (directory is null) return null;
        string path = Path.Combine(directory, SafeArchive.IndexName);
        if (!File.Exists(path)) return null;
        SafeArchive.NoReparse(directory);
        SafeArchive.NoReparse(path);
        if (new FileInfo(path).Length > SafeArchive.MaximumIndexBytes) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<InstallationIndex>(stream, SafeArchive.JsonOptions);
    }

    private static string? ReadInstalledVersion(string? directory)
    {
        try { return ReadIndex(directory)?.Release?.Version; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or LauncherException) { return null; }
    }

    private static async Task<bool> VerifyInstallationAsync(string? directory, ReleaseManifest latest,
        IProgress<LauncherProgress>? progress, CancellationToken cancellationToken)
    {
        if (directory is null) return false;
        try
        {
            InstallationIndex? index = ReadIndex(directory);
            if (index is null || index.Release != latest || index.Files is null || index.Files.Count is < 1 or > ReleaseManifest.MaximumFiles) return false;
            var expected = new Dictionary<string, InstalledFile>(index.Files.Count, StringComparer.OrdinalIgnoreCase);
            long declared = 0;
            foreach (InstalledFile file in index.Files)
            {
                if (file is null || file.Size < 0 || file.Size > ReleaseManifest.MaximumExpandedBytes - declared ||
                    file.Sha256 is null || !Regex.IsMatch(file.Sha256, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)) return false;
                string relative = SafeArchive.RelativePath(file.Path);
                if (!expected.TryAdd(relative, file)) return false;
                declared += file.Size;
            }
            if (!expected.TryGetValue(latest.EntryPoint, out InstalledFile? executable) || executable.Size == 0 ||
                !expected.ContainsKey(ReleaseManifest.BuildFile)) return false;
            var pending = new Stack<string>();
            pending.Push(directory);
            int found = 0;
            long checkedBytes = 0;
            long lastReport = 0;
            while (pending.TryPop(out string? current))
            {
                SafeArchive.NoReparse(current);
                foreach (string path in Directory.EnumerateFileSystemEntries(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SafeArchive.NoReparse(path);
                    if (Directory.Exists(path)) { pending.Push(path); continue; }
                    string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                    if (relative == SafeArchive.IndexName) continue;
                    if (!expected.TryGetValue(relative, out InstalledFile? file)) return false;
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (stream.Length != file.Size) return false;
                    byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (Convert.ToHexStringLower(hash) != file.Sha256) return false;
                    found++;
                    checkedBytes += file.Size;
                    if (Environment.TickCount64 - lastReport >= 100)
                    {
                        progress?.Report(new LauncherProgress("설치된 모든 게임 파일 검증 중", checkedBytes, declared));
                        lastReport = Environment.TickCount64;
                    }
                }
            }
            if (found != expected.Count) return false;
            string build = Path.Combine(directory, ReleaseManifest.BuildFile);
            if (new FileInfo(build).Length > ReleaseManifest.MaximumManifestBytes) return false;
            latest.ValidateBuild(await File.ReadAllBytesAsync(build, cancellationToken).ConfigureAwait(false));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or LauncherException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsGameRunning()
    {
        Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ReleaseManifest.GameExecutable));
        try { return processes.Any(process => !process.HasExited); }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    private static void StartGame(ProcessStartInfo start)
    {
        using Process? process = Process.Start(start);
        if (process is null) throw new LauncherException("게임 프로세스를 시작하지 못했습니다.");
    }

    private static void TryDeleteOwned(string path)
    {
        try { SafeArchive.DeleteOwnedTree(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LauncherException)
        {
            // A locked transaction is left untouched; it never authorizes launch and is never reused.
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        instanceLock.Dispose();
        client.Dispose();
        operation.Dispose();
    }
}
