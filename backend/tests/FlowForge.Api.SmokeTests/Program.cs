using System.Text.Json;
using FlowForge.Api.SmokeTests;

// Run against a disposable API/database: the journey creates users and business records.
if (args is ["--help"] or ["-h"])
{
    Console.WriteLine("Usage: dotnet run --project backend/tests/FlowForge.Api.SmokeTests -- --base-url URL --output FILE");
    return 0;
}
var arguments = new Dictionary<string, string>();
for (var i = 0; i < args.Length; i += 2)
{
    if (i + 1 == args.Length || args[i] is not ("--base-url" or "--output") || !arguments.TryAdd(args[i], args[i + 1]))
    { Console.Error.WriteLine("Supply --base-url URL and --output FILE."); return 2; }
}
if (!arguments.TryGetValue("--base-url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
    uri.Scheme is not ("http" or "https") || !arguments.TryGetValue("--output", out var output) || string.IsNullOrWhiteSpace(output))
{ Console.Error.WriteLine("Supply an absolute HTTP(S) --base-url and an --output file."); return 2; }

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var session = new SmokeSession(client, url);
var exitCode = 0;
try { await new WorkflowApiJourney(session).RunAsync(); }
catch (SmokeCheckFailure failure) { Console.Error.WriteLine(failure.Message); exitCode = 1; }
catch (Exception failure)
{
    // Provider errors may contain addresses or bodies. Keep diagnostics safe, as in the API workers.
    Console.Error.WriteLine($"Smoke test failed ({failure.GetType().Name})."); exitCode = 1;
}
finally
{
    var path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var report = session.Report();
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, SmokeSession.JsonOptions) + Environment.NewLine);
    Console.WriteLine(JsonSerializer.Serialize(new { report.Totals, results = output }, SmokeSession.JsonOptions));
}
return exitCode;
