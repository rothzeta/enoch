using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Enoch.Client.Tests;

public sealed class CliFailureTests
{
    const string TestToken = "fixture-publisher-token";

    [Fact]
    public async Task Transport_failure_has_a_controlled_exit_and_actionable_message()
    {
        var unused = new TcpListener(IPAddress.Loopback, 0);
        unused.Start();
        var address = new Uri($"http://127.0.0.1:{((IPEndPoint)unused.LocalEndpoint).Port}/");
        unused.Stop();
        var result = await RunCli(address, "1", "read", "--run", "run");
        AssertFailure(result, "Unable to reach Enoch");
        Assert.Contains("ENOCH_URL", result.Errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_has_a_controlled_exit_without_a_long_wait(bool responseHeadersReceived)
    {
        await using var server = new ResponseServer(null, responseHeadersReceived);
        var result = await RunCli(server.Address, "0.2", "read", "--run", "run");
        AssertFailure(result, "timed out");
        Assert.True(server.ReceivedRequest);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"manifest\":null}")]
    public async Task Invalid_response_has_a_controlled_exit(string body)
    {
        await using var server = new ResponseServer(body);
        var result = await RunCli(server.Address, "1", "start", "--title", "task", "--request", "-");
        AssertFailure(result, "invalid response");
    }

    [Fact]
    public async Task Result_outcome_is_rejected_without_publishing()
    {
        await using var server = new ResponseServer("{\"runId\":\"run\",\"sequence\":0,\"state\":\"running\"}");
        var input = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(input, "result");
            var result = await RunCli(server.Address, "1", "result", "--run", "run", "--file", input, "--outcome", "failed");
            AssertFailure(result, "Use finish --outcome");
            Assert.False(server.ReceivedRequest);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("invalid")]
    public async Task Invalid_timeout_is_rejected_before_network(string timeout)
    {
        await using var server = new ResponseServer("{\"manifest\":{\"id\":\"run\",\"state\":\"running\"}}");
        var result = await RunCli(server.Address, timeout, "read", "--run", "run");
        AssertFailure(result, "ENOCH_TIMEOUT_SECONDS");
        Assert.False(server.ReceivedRequest);
    }

    static void AssertFailure(CliResult result, string message)
    {
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Errors, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unhandled exception", result.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", result.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain(TestToken, result.Errors, StringComparison.Ordinal);
    }

    static async Task<CliResult> RunCli(Uri address, string timeout, params string[] arguments)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Enoch.Cli.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["ENOCH_URL"] = address.AbsoluteUri;
        start.Environment["ENOCH_TOKEN"] = TestToken;
        start.Environment["ENOCH_TIMEOUT_SECONDS"] = timeout;
        using var process = Process.Start(start)!;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var output = process.StandardOutput.ReadToEndAsync(budget.Token);
        var errors = process.StandardError.ReadToEndAsync(budget.Token);
        try
        {
            await process.StandardInput.WriteAsync("request");
            process.StandardInput.Close();
            await process.WaitForExitAsync(budget.Token);
            return new CliResult(process.ExitCode, await output, await errors);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    sealed record CliResult(int ExitCode, string Output, string Errors);

    sealed class ResponseServer : IAsyncDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource shutdown = new();
        readonly Task serving;
        readonly string? body;
        readonly bool sendHeadersBeforeDelay;
        int receivedRequests;

        public ResponseServer(string? body, bool sendHeadersBeforeDelay = false)
        {
            this.body = body;
            this.sendHeadersBeforeDelay = sendHeadersBeforeDelay;
            listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            serving = ServeAsync();
        }

        public Uri Address { get; }
        public bool ReceivedRequest => Volatile.Read(ref receivedRequests) > 0;

        async Task ServeAsync()
        {
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    using var connection = await listener.AcceptTcpClientAsync(shutdown.Token);
                    await using var stream = connection.GetStream();
                    var buffer = new byte[4096];
                    await stream.ReadAsync(buffer, shutdown.Token);
                    Interlocked.Increment(ref receivedRequests);
                    if (body is null)
                    {
                        if (sendHeadersBeforeDelay)
                        {
                            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1024\r\n\r\n"), shutdown.Token);
                        }
                        await Task.Delay(Timeout.Infinite, shutdown.Token);
                    }
                    else
                    {
                        var content = Encoding.UTF8.GetBytes(body);
                        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(headers, shutdown.Token);
                        await stream.WriteAsync(content, shutdown.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                // The owning test cancels the listener and any delayed response.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await shutdown.CancelAsync();
            try
            {
                await serving.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                listener.Stop();
                shutdown.Dispose();
            }
        }
    }
}
