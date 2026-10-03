using System.Diagnostics;
using Xunit;

namespace Enoch.Client.Tests;

public sealed class CliSmokeTests
{
    [Fact]
    public async Task Help_is_available_without_server_configuration()
    {
        var cli = Path.Combine(AppContext.BaseDirectory, "Enoch.Cli.dll");
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(host, $"\"{cli}\" --help")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var errors = await process.StandardError.ReadToEndAsync();
        Assert.True(process.ExitCode == 0, errors);
        Assert.Contains("Environment: ENOCH_URL", output);
        Assert.Contains("start --title", output);
    }
}
