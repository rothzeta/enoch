using System.Text.Json;
using System.Globalization;
using Enoch.Client;

return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Help();
            return 0;
        }
        try
        {
            using var client = Client();
            var command = args[0].ToLowerInvariant();
            var runId = Value(args, "--run") ?? Environment.GetEnvironmentVariable("ENOCH_RUN_ID");
            switch (command)
            {
                case "start":
                    var started = await client.StartRunAsync(Required(args, "--title"), await ReadInput(args, "--request"));
                    Console.WriteLine(started.RunId);
                    return 0;
                case "plan":
                    var plan = await client.PublishPlanAsync(RequiredRun(runId), await ReadInput(args, "--file"), Value(args, "--format") ?? "markdown");
                    Print(plan);
                    return 0;
                case "progress":
                    var progress = await client.PublishProgressAsync(RequiredRun(runId), Required(args, "--message"), Value(args, "--event-id"), Value(args, "--kind") ?? "progress");
                    Print(progress);
                    return 0;
                case "evidence":
                    var evidence = await client.AddEvidenceAsync(RequiredRun(runId), Value(args, "--name") ?? "evidence.txt", await ReadInput(args, "--file"), Value(args, "--content-type") ?? "text/plain");
                    Print(evidence);
                    return 0;
                case "artifact":
                    var artifactPath = Required(args, "--file");
                    Print(await client.AddArtifactAsync(RequiredRun(runId), artifactPath, Value(args, "--name"), Value(args, "--content-type")));
                    return 0;
                case "result":
                    if (args.Contains("--outcome", StringComparer.Ordinal))
                    {
                        throw new ArgumentException("result does not accept --outcome. Use finish --outcome after publishing the result.");
                    }
                    Print(await client.PublishResultAsync(RequiredRun(runId), await ReadInput(args, "--file")));
                    return 0;
                case "finish":
                    Print(await client.FinishRunAsync(RequiredRun(runId), Required(args, "--outcome"), Value(args, "--summary")));
                    return 0;
                case "read":
                    using (var doc = await client.ReadRunAsync(RequiredRun(runId)))
                    {
                        Console.WriteLine(doc.RootElement.GetRawText());
                    }

                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'. Use 'help'.");
                    return 2;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Unable to reach Enoch: {ex.Message}. Check ENOCH_URL and connectivity.");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("The Enoch request timed out. Check ENOCH_URL and server availability, or increase ENOCH_TIMEOUT_SECONDS.");
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or EnochApiException or IOException or JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static EnochClient Client()
    {
        var url = Environment.GetEnvironmentVariable("ENOCH_URL");
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("ENOCH_URL must be an absolute HTTP or HTTPS URL.");
        }

        var timeout = TimeSpan.FromSeconds(100);
        var configuredTimeout = Environment.GetEnvironmentVariable("ENOCH_TIMEOUT_SECONDS");
        if (configuredTimeout is not null)
        {
            if (!double.TryParse(configuredTimeout, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                !double.IsFinite(seconds) || seconds <= 0 || seconds > int.MaxValue / 1000d)
            {
                throw new ArgumentException("ENOCH_TIMEOUT_SECONDS must be a positive finite number of seconds.");
            }
            timeout = TimeSpan.FromSeconds(seconds);
        }
        return new EnochClient(new EnochClientOptions { BaseAddress = uri, Token = Environment.GetEnvironmentVariable("ENOCH_TOKEN"), RequestTimeout = timeout });
    }
    private static string RequiredRun(string? id) => !string.IsNullOrWhiteSpace(id) ? id : throw new ArgumentException("Set ENOCH_RUN_ID or pass --run.");
    private static string Required(string[] args, string option) => Value(args, option) ?? throw new ArgumentException($"Missing {option}.");
    private static string? Value(string[] args, string option)
    {
        var i = Array.IndexOf(args, option);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    private static async Task<string> ReadInput(string[] args, string option)
    {
        var path = Required(args, option);
        return path == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(path);
    }
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    private static void Help() => Console.WriteLine("Enoch CLI\n\nEnvironment: ENOCH_URL, ENOCH_TOKEN, ENOCH_RUN_ID, ENOCH_TIMEOUT_SECONDS (optional; default 100)\n\nCommands:\n  start --title TITLE --request FILE\n  plan --file FILE [--run ID]\n  progress --message TEXT [--event-id ID] [--run ID]\n  evidence --name NAME --file FILE [--run ID]\n  artifact --file FILE [--name NAME] [--run ID]\n  result --file FILE [--run ID]\n  finish --outcome success|partial|failed|cancelled|expired [--summary TEXT] [--run ID]\n  read [--run ID]");
}
