using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BellRun.Launcher.Core;

namespace BellRun.Launcher.Tests;

internal static class Program
{
    private static int passed;
    private static int failed;

    private static async Task<int> Main()
    {
        await Run("strict release metadata", MetadataValidationAsync);
        await Run("HTTP download, install, hashes, fresh play checks", SuccessfulInstallAsync);
        await Run("tampered installed file is repaired before execution", RepairAsync);
        await Run("unexpected installed file is rejected and repaired", UnexpectedFileAsync);
        await Run("missing hash index cannot authorize local marker", MissingIndexAsync);
        await Run("offline check never executes previously verified game", OfflineAsync);
        await Run("new release during play blocks execution", StaleAsync);
        await Run("failed final check after install blocks execution", FinalCheckFailureAsync);
        await Run("archive corruption preserves old install", CorruptDownloadAsync);
        await Run("archive size mismatch preserves old install", WrongSizeAsync);
        await Run("embedded version mismatch preserves old install", () => InvalidArchiveAsync(CreateZip("9.0.0")));
        await Run("embedded protocol mismatch preserves old install", () => InvalidArchiveAsync(CreateZip("1.0.1", protocol: 36)));
        await Run("missing executable preserves old install", () => InvalidArchiveAsync(CreateZip("1.0.1", includeExecutable: false)));
        await Run("zip traversal rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("../escape.txt", Bytes("bad"), 0)])));
        await Run("zip absolute path rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("/escape.txt", Bytes("bad"), 0)])));
        await Run("zip backslash traversal rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("..\\escape.txt", Bytes("bad"), 0)])));
        await Run("zip drive path rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("C:/escape.txt", Bytes("bad"), 0)])));
        await Run("case-insensitive duplicate rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("halligallimovement.EXE", Bytes("bad"), 0)])));
        await Run("zip symbolic link rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("link", Bytes("../escape"), unchecked((int)0xa1ff0000))])));
        await Run("zip reparse point rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("link", Bytes("bad"), (int)FileAttributes.ReparsePoint)])));
        await Run("Windows device path rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("data/CON.txt", Bytes("bad"), 0)])));
        await Run("Windows alternate stream rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("data/file:stream", Bytes("bad"), 0)])));
        await Run("file directory collision rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [("collision", Bytes("bad"), 0), ("collision/file", Bytes("bad"), 0)])));
        await Run("launcher index overwrite rejected", () => InvalidArchiveAsync(CreateZip("1.0.1", extras: [(SafeArchive.IndexName, Bytes("bad"), 0)])));
        await Run("expanded size limit rejected", () => InvalidArchiveAsync(OversizedZip()));
        await Run("untrusted redirect rejected", RedirectAsync);
        await Run("oversized remote manifest rejected", OversizedManifestAsync);
        await Run("running game blocks update and launch", RunningGameAsync);
        await Run("second launcher instance rejected", SingleInstanceAsync);
        await Run("game started during download prevents promotion", GameStartedDuringDownloadAsync);
        await Run("cancellation cleans only owned staging", CancellationAsync);
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static async Task Run(string name, Func<Task> test)
    {
        try { await test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
    }

    private static Task MetadataValidationAsync()
    {
        ReleaseManifest good = Manifest(CreateZip("1.0.0"), "1.0.0");
        Assert(ReleaseManifest.Parse(Serialize(good)) == good, "valid metadata rejected");
        ReleaseManifest[] bad =
        [
            good with { SchemaVersion = 2 }, good with { Version = "01.0.0" }, good with { Version = "1.0.0-beta" },
            good with { Version = "1.0.0\n" }, good with { Protocol = 0 }, good with { ArchiveName = "../game.zip" },
            good with { DownloadUrl = "https://evil.example/game.zip" },
            good with { DownloadUrl = good.DownloadUrl + "?redirect=evil" },
            good with { DownloadUrl = good.DownloadUrl.Replace("https:", "http:") },
            good with { Sha256 = new string('A', 64) }, good with { Size = 0 },
            good with { Size = ReleaseManifest.MaximumArchiveBytes + 1 }, good with { EntryPoint = "evil.exe" }
        ];
        foreach (ReleaseManifest manifest in bad)
        {
            bool rejected = false;
            try { ReleaseManifest.Parse(Serialize(manifest)); }
            catch (LauncherException) { rejected = true; }
            Assert(rejected, "unsafe metadata accepted: " + manifest);
        }
        string json = Encoding.UTF8.GetString(Serialize(good));
        bool duplicateRejected = false;
        try { ReleaseManifest.Parse(Bytes(json[..^1] + ",\"version\":\"1.0.0\"}")); }
        catch (LauncherException) { duplicateRejected = true; }
        Assert(duplicateRejected, "duplicate JSON property accepted");
        return Task.CompletedTask;
    }

    private static async Task SuccessfulInstallAsync()
    {
        await using var env = new EnvironmentFixture();
        LauncherState before = await env.Engine.CheckAsync();
        Assert(!before.IsReady, "empty install is ready");
        LauncherState installed = await env.Engine.InstallLatestAsync();
        Assert(installed.IsReady && installed.InstalledVersion == "1.0.0", "verified install missing");
        int requests = env.ManifestRequests;
        await env.Engine.PlayAsync();
        Assert(env.ManifestRequests == requests + 2, "play did not fetch twice");
        Assert(env.Starts.Count == 1, "verified process not invoked exactly once");
        ProcessStartInfo start = env.Starts[0];
        Assert(File.ReadAllBytes(start.FileName).SequenceEqual(GameBytes), "wrong installed bytes");
        Assert(!start.UseShellExecute && start.WorkingDirectory == Path.GetDirectoryName(start.FileName), "unsafe launch parameters");
        Assert(File.Exists(Path.Combine(start.WorkingDirectory, SafeArchive.IndexName)), "hash index missing");
        lock (env.Server.RequestHeaders)
        {
            Assert(env.Server.RequestHeaders.All(header => header.Contains("User-Agent: BellRunLauncher/1.0.0", StringComparison.OrdinalIgnoreCase)), "User-Agent missing");
            string[] fresh = env.Server.RequestHeaders.Where(header => header.Contains("/latest/download/")).ToArray();
            Assert(fresh.All(header => header.Contains("no-cache") && header.Contains("bellrun-check=")), "freshness headers missing");
            Assert(fresh.Select(header => header.Split('\n')[0]).Distinct().Count() == fresh.Length, "manifest URL was reused");
        }
    }

    private static async Task RepairAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        File.WriteAllBytes(Path.Combine(old, ReleaseManifest.GameExecutable), Bytes("tampered"));
        LauncherState state = await env.Engine.CheckAsync();
        Assert(!state.IsReady, "tampered file authorized");
        int downloads = env.DownloadRequests;
        await env.Engine.PlayAsync();
        Assert(env.DownloadRequests == downloads + 1 && env.Starts.Count == 1, "repair did not download before launch");
        Assert(env.ActiveDirectory != old && Directory.Exists(old), "old installation was replaced");
        Assert(File.ReadAllBytes(env.Starts[0].FileName).SequenceEqual(GameBytes), "tampered game launched");
    }

    private static async Task UnexpectedFileAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        File.WriteAllText(Path.Combine(env.ActiveDirectory, "unexpected.dll"), "untrusted");
        Assert(!(await env.Engine.CheckAsync()).IsReady, "unlisted executable content allowed");
        await env.Engine.PlayAsync();
        Assert(!File.Exists(Path.Combine(env.Starts.Single().WorkingDirectory, "unexpected.dll")), "unlisted file carried into repair");
    }

    private static async Task MissingIndexAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        File.Delete(Path.Combine(old, SafeArchive.IndexName));
        Assert(!(await env.Engine.CheckAsync()).IsReady, "active marker alone authorized launch");
        await env.Engine.PlayAsync();
        Assert(env.ActiveDirectory != old && env.Starts.Count == 1, "missing index was not repaired");
    }

    private static async Task OfflineAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        env.ManifestReply = _ => new FixtureReply([], 503);
        await Blocked(() => env.Engine.PlayAsync());
        Assert(env.Starts.Count == 0, "cached authorization launched offline");
    }

    private static async Task StaleAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        ReleaseManifest newer = Manifest(CreateZip("1.0.1"), "1.0.1");
        int count = 0;
        env.ManifestReply = _ => new FixtureReply(Serialize(++count == 1 ? env.Release : newer));
        await Blocked(() => env.Engine.PlayAsync());
        Assert(count == 2 && env.Starts.Count == 0, "stale final check launched");
    }

    private static async Task FinalCheckFailureAsync()
    {
        await using var env = new EnvironmentFixture();
        int count = 0;
        env.ManifestReply = _ => ++count == 1 ? new FixtureReply(Serialize(env.Release)) : new FixtureReply([], 503);
        await Blocked(() => env.Engine.PlayAsync());
        Assert(env.DownloadRequests == 1 && env.Starts.Count == 0, "failed final check executed freshly downloaded game");
    }

    private static async Task CorruptDownloadAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        byte[] archive = CreateZip("1.0.1");
        env.SetRelease(archive, "1.0.1");
        env.Archive = (byte[])archive.Clone();
        env.Archive[env.Archive.Length / 2] ^= 0xff;
        await Blocked(() => env.Engine.InstallLatestAsync());
        AssertOldPreserved(env, old);
    }

    private static async Task WrongSizeAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        env.SetRelease(CreateZip("1.0.1"), "1.0.1");
        env.Release = env.Release with { Size = env.Release.Size + 1 };
        await Blocked(() => env.Engine.InstallLatestAsync());
        AssertOldPreserved(env, old);
    }

    private static async Task InvalidArchiveAsync(byte[] archive)
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        env.SetRelease(archive, "1.0.1");
        await Blocked(() => env.Engine.InstallLatestAsync());
        AssertOldPreserved(env, old);
        Assert(!File.Exists(Path.Combine(env.Root, "escape.txt")), "traversal wrote outside staging");
        Assert(!Directory.EnumerateDirectories(env.Root, "work-*").Any(), "owned failed staging was not cleaned");
    }

    private static async Task RedirectAsync()
    {
        await using var env = new EnvironmentFixture();
        env.ManifestReply = _ => new FixtureReply([], 302, "https://evil.example/BellRun-release.json");
        await Blocked(() => env.Engine.PlayAsync());
        Assert(env.Starts.Count == 0 && env.ManifestRequests == 1, "untrusted redirect followed");
    }

    private static async Task OversizedManifestAsync()
    {
        await using var env = new EnvironmentFixture();
        env.ManifestReply = _ => new FixtureReply(new byte[ReleaseManifest.MaximumManifestBytes + 1]);
        await Blocked(() => env.Engine.PlayAsync());
        Assert(env.Starts.Count == 0 && env.DownloadRequests == 0, "oversized metadata authorized");
    }

    private static async Task RunningGameAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        env.Running = true;
        env.SetRelease(CreateZip("1.0.1"), "1.0.1");
        int downloads = env.DownloadRequests;
        await Blocked(() => env.Engine.InstallLatestAsync());
        await Blocked(() => env.Engine.PlayAsync());
        Assert(env.DownloadRequests == downloads && env.Starts.Count == 0, "running game updated or relaunched");
        AssertOldPreserved(env, old);
    }

    private static async Task SingleInstanceAsync()
    {
        await using var env = new EnvironmentFixture();
        bool blocked = false;
        try { using var second = new LauncherEngine(env.Root, env.Server.CreateClient(), () => false, _ => { }); }
        catch (LauncherException) { blocked = true; }
        Assert(blocked, "second installer acquired same root");
    }

    private static async Task GameStartedDuringDownloadAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        env.SetRelease(CreateZip("1.0.1"), "1.0.1");
        env.DownloadHook = () => env.Running = true;
        await Blocked(() => env.Engine.InstallLatestAsync());
        AssertOldPreserved(env, old);
    }

    private static async Task CancellationAsync()
    {
        await using var env = new EnvironmentFixture();
        await env.Engine.InstallLatestAsync();
        string old = env.ActiveDirectory;
        string unknown = Path.Combine(env.Root, "work-user-owned");
        Directory.CreateDirectory(unknown);
        File.WriteAllText(Path.Combine(unknown, "keep.txt"), "keep");
        env.SetRelease(CreateZip("1.0.1"), "1.0.1");
        env.DownloadDelay = 5000;
        using var cancellation = new CancellationTokenSource();
        Task installation = env.Engine.InstallLatestAsync(cancellationToken: cancellation.Token);
        await env.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Blocked(() => env.Engine.PlayAsync());
        cancellation.Cancel();
        bool canceled = false;
        try { await installation; }
        catch (OperationCanceledException) { canceled = true; }
        Assert(canceled, "cancellation not propagated");
        AssertOldPreserved(env, old);
        Assert(File.ReadAllText(Path.Combine(unknown, "keep.txt")) == "keep", "unknown directory was deleted");
        Assert(Directory.EnumerateDirectories(env.Root, "work-*").Single() == unknown, "transaction staging remained");
    }

    private static void AssertOldPreserved(EnvironmentFixture env, string old)
    {
        Assert(env.ActiveDirectory == old, "active pointer changed after failed install");
        Assert(File.ReadAllBytes(Path.Combine(old, ReleaseManifest.GameExecutable)).SequenceEqual(GameBytes), "old game modified");
        Assert(File.ReadAllText(env.UserMap) == "persistent-user-map", "user data changed");
        Assert(env.Starts.Count == 0, "failed operation launched a game");
    }

    private static async Task Blocked(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex) when (ex is LauncherException or HttpRequestException or InvalidDataException) { return; }
        throw new InvalidOperationException("operation unexpectedly succeeded");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static readonly byte[] GameBytes = Bytes("MZ fixture executable contents — ProcessStart delegate prevents execution");
    private static byte[] Serialize(ReleaseManifest release) => JsonSerializer.SerializeToUtf8Bytes(release, SafeArchive.JsonOptions);
    private static ReleaseManifest Manifest(byte[] archive, string version) => new(1, version, 35,
        $"BellRun-Windows-{version}.zip", $"{ReleaseManifest.Repository}/releases/download/v{version}/BellRun-Windows-{version}.zip",
        Convert.ToHexStringLower(SHA256.HashData(archive)), archive.LongLength, ReleaseManifest.GameExecutable);

    private static byte[] CreateZip(string version, int protocol = 35, bool includeExecutable = true,
        (string Path, byte[] Bytes, int Attributes)[]? extras = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var files = new List<(string Path, byte[] Bytes, int Attributes)>();
            if (includeExecutable) files.Add((ReleaseManifest.GameExecutable, GameBytes, 0));
            files.Add((ReleaseManifest.BuildFile, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, version, protocol, entryPoint = ReleaseManifest.GameExecutable }), 0));
            files.Add(("HalliGalliMovement_Data/game.dat", Bytes("real content hash fixture"), 0));
            if (extras is not null) files.AddRange(extras);
            foreach (var file in files)
            {
                ZipArchiveEntry entry = zip.CreateEntry(file.Path, CompressionLevel.Fastest);
                entry.ExternalAttributes = file.Attributes;
                using Stream stream = entry.Open();
                stream.Write(file.Bytes);
            }
        }
        return output.ToArray();
    }

    private static byte[] OversizedZip()
    {
        byte[] bytes = CreateZip("1.0.1", extras: [("large-one", [], 0), ("large-two", [], 0)]);
        for (int offset = 0; offset + 46 < bytes.Length; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) == 0x02014b50)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24, 4), 0xfffffff0);
        }
        return bytes;
    }

    private sealed class EnvironmentFixture : IAsyncDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "BellRun-launcher-tests-" + Guid.NewGuid().ToString("N"));
        internal HttpFixture Server { get; } = new();
        internal LauncherEngine Engine { get; }
        internal List<ProcessStartInfo> Starts { get; } = [];
        internal byte[] Archive { get; set; }
        internal ReleaseManifest Release { get; set; }
        internal Func<int, FixtureReply>? ManifestReply { get; set; }
        internal bool Running { get; set; }
        internal int ManifestRequests { get; private set; }
        internal int DownloadRequests { get; private set; }
        internal int DownloadDelay { get; set; }
        internal Action? DownloadHook { get; set; }
        internal TaskCompletionSource DownloadStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string UserMap => Path.Combine(Root, "separate-user-data", "user.bellmap");
        internal string ActiveDirectory => Path.Combine(Root, "versions", File.ReadAllText(Path.Combine(Root, "active-install.txt")));

        internal EnvironmentFixture()
        {
            Archive = CreateZip("1.0.0");
            Release = Manifest(Archive, "1.0.0");
            Engine = new LauncherEngine(Root, Server.CreateClient(), () => Running, info => Starts.Add(info));
            Directory.CreateDirectory(Path.GetDirectoryName(UserMap)!);
            File.WriteAllText(UserMap, "persistent-user-map");
            Server.Respond = path =>
            {
                if (path.StartsWith("/alphamon1114/halligalli-deathrun/releases/latest/download/BellRun-release.json?", StringComparison.Ordinal))
                {
                    ManifestRequests++;
                    return ManifestReply?.Invoke(ManifestRequests) ?? new FixtureReply(Serialize(Release));
                }
                if (path == new Uri(Release.DownloadUrl).AbsolutePath)
                {
                    DownloadRequests++;
                    DownloadHook?.Invoke();
                    DownloadStarted.TrySetResult();
                    return new FixtureReply(Archive, DelayMilliseconds: DownloadDelay);
                }
                return new FixtureReply([], 404);
            };
        }

        internal void SetRelease(byte[] archive, string version)
        {
            Archive = archive;
            Release = Manifest(archive, version);
            DownloadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async ValueTask DisposeAsync()
        {
            Engine.Dispose();
            await Server.DisposeAsync();
            SafeArchive.DeleteOwnedTree(Root);
        }
    }
}
