using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace BellRun.Launcher.Core;

public sealed record LauncherProgress(string Message, long CompletedBytes = 0, long TotalBytes = 0);

internal sealed class ReleaseTransport
{
    private readonly HttpClient client;

    internal ReleaseTransport(HttpClient client) => this.client = client;

    internal static HttpClient CreateClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal async Task<ReleaseManifest> FetchLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var freshAddress = new Uri(ReleaseManifest.LatestUrl + "?bellrun-check=" + Guid.NewGuid().ToString("N"));
            using var response = await GetAsync(freshAddress, true, timeout.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength > ReleaseManifest.MaximumManifestBytes)
                throw new LauncherException("배포 정보가 허용 크기를 초과했습니다.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(0, 8192), timeout.Token).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + count > ReleaseManifest.MaximumManifestBytes)
                        throw new LauncherException("배포 정보가 허용 크기를 초과했습니다.");
                    output.Write(buffer, 0, count);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            return ReleaseManifest.Parse(output.GetBuffer().AsMemory(0, checked((int)output.Length)));
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LauncherException("최신 버전 확인 시간이 초과되었습니다. 인터넷 연결 후 다시 시도하세요. 오프라인 실행은 허용되지 않습니다.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LauncherException("GitHub에서 최신 버전을 확인하지 못했습니다. 인터넷 연결과 공개 Release를 확인하고 다시 시도하세요. 실행은 차단됩니다.", ex);
        }
    }

    internal async Task DownloadAsync(ReleaseManifest release, string destination,
        IProgress<LauncherProgress>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(45));
        using var response = await GetAsync(new Uri(release.DownloadUrl), false, timeout.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is long length && length != release.Size)
            throw new LauncherException("다운로드 크기가 배포 정보와 다릅니다.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long total = 0;
        long lastReport = 0;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        try
        {
            while (true)
            {
                idle.CancelAfter(TimeSpan.FromSeconds(60));
                int count = await input.ReadAsync(buffer.AsMemory(0, 128 * 1024), idle.Token).ConfigureAwait(false);
                idle.CancelAfter(Timeout.InfiniteTimeSpan);
                if (count == 0) break;
                total += count;
                if (total > release.Size)
                    throw new LauncherException("다운로드가 선언된 크기를 초과했습니다.");
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                if (Environment.TickCount64 - lastReport >= 100 || total == release.Size)
                {
                    progress?.Report(new LauncherProgress("게임 다운로드 중", total, release.Size));
                    lastReport = Environment.TickCount64;
                }
            }
            if (total != release.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != release.Sha256)
                throw new LauncherException("다운로드 파일의 크기 또는 SHA-256 검증에 실패했습니다. 기존 게임은 유지됩니다.");
            await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            output.Flush(true);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LauncherException("다운로드가 지연되어 중단했습니다. 기존 게임은 유지됩니다. 다시 시도하세요.", ex);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri initial, bool manifest, CancellationToken cancellationToken)
    {
        Uri address = initial;
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            ValidateAddress(address, manifest, initial);
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.UserAgent.ParseAdd("BellRunLauncher/1.0.0");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true, MaxAge = TimeSpan.Zero };
            request.Headers.Pragma.ParseAdd("no-cache");
            request.Headers.Accept.ParseAdd(manifest ? "application/json" : "application/octet-stream");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is 301 or 302 or 303 or 307 or 308)
            {
                Uri? location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                    throw new LauncherException("다운로드 리디렉션 주소가 없습니다.");
                address = location.IsAbsoluteUri ? location : new Uri(address, location);
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.Dispose();
                throw new HttpRequestException($"GitHub HTTP {status}");
            }
            if (response.Content.Headers.ContentEncoding.Count != 0)
            {
                response.Dispose();
                throw new LauncherException("예상하지 못한 다운로드 인코딩입니다.");
            }
            return response;
        }
        throw new LauncherException("다운로드 리디렉션이 너무 많습니다.");
    }

    private static void ValidateAddress(Uri address, bool manifest, Uri initial)
    {
        if (address.Scheme != Uri.UriSchemeHttps || !address.IsDefaultPort ||
            address.UserInfo.Length != 0 || address.Fragment.Length != 0)
            throw new LauncherException("안전하지 않은 다운로드 주소를 차단했습니다.");
        if (address.Host == "release-assets.githubusercontent.com") return;
        if (address == initial) return;
        if (address.Host != "github.com" || (address.Query.Length != 0 && address.Query != initial.Query))
            throw new LauncherException("신뢰하지 않는 다운로드 호스트를 차단했습니다.");
        if (manifest)
        {
            const string prefix = "/alphamon1114/halligalli-deathrun/releases/download/v";
            const string suffix = "/BellRun-release.json";
            string path = address.AbsolutePath;
            if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(suffix, StringComparison.Ordinal) &&
                ReleaseManifest.IsVersion(path[prefix.Length..^suffix.Length])) return;
        }
        throw new LauncherException("다른 저장소 또는 파일로의 리디렉션을 차단했습니다.");
    }
}
