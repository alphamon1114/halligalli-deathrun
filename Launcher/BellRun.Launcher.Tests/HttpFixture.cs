using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BellRun.Launcher.Tests;

internal sealed record FixtureReply(byte[] Body, int Status = 200, string? Location = null, int DelayMilliseconds = 0);

internal sealed class HttpFixture : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task serving;
    private readonly List<Task> connections = [];
    internal Func<string, FixtureReply> Respond { get; set; } = _ => new FixtureReply([], 404);
    internal List<string> RequestHeaders { get; } = [];
    private Uri Address { get; }

    internal HttpFixture()
    {
        listener.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        serving = ServeAsync();
    }

    internal HttpClient CreateClient() => new(new LocalRoutingHandler(Address)) { Timeout = Timeout.InfiniteTimeSpan };

    private async Task ServeAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                TcpClient socket = await listener.AcceptTcpClientAsync(lifetime.Token);
                connections.Add(HandleAsync(socket));
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient socket)
    {
        using (socket)
        {
            try
            {
                await using NetworkStream stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                string request = await reader.ReadLineAsync(lifetime.Token) ?? "";
                var headers = new StringBuilder(request);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(lifetime.Token))) headers.Append('\n').Append(line);
                lock (RequestHeaders) RequestHeaders.Add(headers.ToString());
                string path = request.Split(' ')[1];
                FixtureReply reply = Respond(path);
                if (reply.DelayMilliseconds > 0) await Task.Delay(reply.DelayMilliseconds, lifetime.Token);
                string responseHeaders = $"HTTP/1.1 {reply.Status} Fixture\r\nContent-Length: {reply.Body.Length}\r\nConnection: close\r\n";
                if (reply.Location is not null) responseHeaders += $"Location: {reply.Location}\r\n";
                responseHeaders += "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHeaders), lifetime.Token);
                await stream.WriteAsync(reply.Body, lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (IOException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        listener.Stop();
        await serving;
        await Task.WhenAll(connections);
        lifetime.Dispose();
    }

    // Only friend test assemblies can inject this routing. Production requests always use the fixed HTTPS URL.
    private sealed class LocalRoutingHandler(Uri address) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(address, request.RequestUri!.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
