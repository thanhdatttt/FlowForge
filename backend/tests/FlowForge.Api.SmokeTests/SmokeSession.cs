using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FlowForge.Api.SmokeTests;

// Only method/path/status and boolean assertions enter the ledger; tokens and bodies stay in memory.
public sealed partial class SmokeSession(HttpClient client, string baseUrl)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly string started = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7))
        .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    private readonly List<SmokeCheck> checks = [];
    public List<SmokeCheck> Requests { get; } = [];

    public async Task<JsonNode> CallAsync(string name, string method, string path, object? body = null,
        string? token = null, int expected = 200, bool record = true)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), baseUrl.TrimEnd('/') + path);
        if (body != null) request.Content = JsonContent.Create(body);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        var status = (int)response.StatusCode;
        var result = new SmokeCheck("http", name, expected, status, status == expected, method, Route(path), !record);
        Requests.Add(result);
        if (record) checks.Add(result);
        if (!result.Passed) throw new SmokeCheckFailure($"{name}: expected {expected}, received {status}.");
        var raw = await response.Content.ReadAsStringAsync();
        return raw.StartsWith('{') || raw.StartsWith('[') ? JsonNode.Parse(raw)! : new JsonObject();
    }
    public void State(string name, bool condition)
    {
        checks.Add(new("state", name, true, condition, condition));
        if (!condition) throw new SmokeCheckFailure(name + ": state assertion failed.");
    }
    public async Task<JsonNode> WaitForExecutionAsync(string path, int count, string token)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            var result = await CallAsync("Poll workflow execution", "GET", path + "/executions", token: token, record: false);
            if (result["total"]!.GetValue<int>() == count && result["items"]!.AsArray().All(x => x!["status"]!.GetValue<string>() == "Succeeded"))
                return result;
            await Task.Delay(200);
        }
        throw new SmokeCheckFailure("Workflow completion timeout.");
    }
    public SmokeReport Report() => new(started, baseUrl.TrimEnd('/'), checks, Requests,
        new(checks.Count, checks.Count(x => x.Passed), checks.Count(x => !x.Passed), checks.Count(x => x.Kind == "http"),
            checks.Count(x => x.Kind == "state"), Requests.Count, Requests.Count(x => x.Polling == true)));

    public static string Route(string path)
    {
        foreach (var (segment, name) in new[] { ("workspaces", "workspaceId"), ("projects", "projectId"),
            ("work-items", "workItemId"), ("workflows", "workflowId"), ("executions", "executionId") })
            path = Regex.Replace(path, "/" + segment + "/[0-9a-fA-F-]{36}", "/" + segment + "/{" + name + "}");
        return QueryIdRegex().Replace(path, match => match.Groups[1].Value + match.Groups[2].Value + "={" + match.Groups[2].Value + "}");
    }
    [GeneratedRegex(@"([?&])(workspaceId|projectId|workItemId|workflowId|executionId)=[0-9a-fA-F-]{36}", RegexOptions.CultureInvariant)]
    private static partial Regex QueryIdRegex();
}
