using System.Diagnostics;
using System.Text.Json.Nodes;

namespace FlowForge.Api.SmokeTests;

public sealed class WorkflowApiJourney(SmokeSession session)
{
    public async Task RunAsync()
    {
        await session.CallAsync("API readiness", "GET", "/health/ready");
        var contract = await session.CallAsync("OpenAPI workflow routes", "GET", "/openapi/v1.json");
        session.State("Workflow management appears in OpenAPI", contract["paths"]!.AsObject().Any(x => x.Key.Contains("/workflows", StringComparison.Ordinal)));
        var login = await RegisterAsync("Register owner", "phase5-", "Phase Five");
        var token = Text(login, "accessToken");
        var developerLogin = await RegisterAsync("Register developer", "phase5-dev-", "Phase Five Developer");
        var developerToken = Text(developerLogin, "accessToken");
        var developer = await session.CallAsync("Read developer account", "GET", "/api/v1/auth/me", token: developerToken);
        var outsiderLogin = await RegisterAsync("Register outsider", "phase5-other-", "Phase Five Outsider");
        var outsiderToken = Text(outsiderLogin, "accessToken");
        var outsider = await session.CallAsync("Read outsider account", "GET", "/api/v1/auth/me", token: outsiderToken);
        var owner = await session.CallAsync("Read signed-in account", "GET", "/api/v1/auth/me", token: token);
        var workspace = await session.CallAsync("Create workspace", "POST", "/api/v1/workspaces", new { name = "Workflow live audit" }, token, 201);
        var wp = "/api/v1/workspaces/" + Text(workspace, "id");
        var project = await session.CallAsync("Create project", "POST", wp + "/projects", new { name = "Workflow audit", projectKey = "WF" }, token, 201);
        var pp = wp + "/projects/" + Text(project, "id");
        await session.CallAsync("Add workspace developer", "POST", wp + "/members", new { email = Text(developer, "email"), role = "Developer" }, token, 201);
        var workflows = wp + "/workflows";
        var definition = new JsonObject
        {
            ["trigger"] = "WorkItemCreated",
            ["conditions"] = new JsonArray(new JsonObject { ["field"] = "Priority", ["operator"] = "Equals", ["values"] = new JsonArray("Critical") }),
            ["actions"] = new JsonArray(Action("ChangePriority", "High"), Action("ChangeStatus", "InProgress"),
                Action("AddComment", "Live workflow completed."), Action("Notify", userId: Text(owner, "id")))
        };
        var body = new JsonObject { ["name"] = "Critical work", ["projectId"] = Text(project, "id"), ["definition"] = definition.DeepClone() };
        await session.CallAsync("Validate workflow", "POST", workflows + "/validate", body, token);
        var bad = body.DeepClone(); bad["definition"]!["trigger"] = "RunScript";
        await session.CallAsync("Reject invalid trigger", "POST", workflows + "/validate", bad, token, 400);
        await session.CallAsync("Require authentication", "GET", workflows, expected: 401);
        await session.CallAsync("Reject developer workflow management", "POST", workflows, body, developerToken, 403);
        var invalidMention = body.DeepClone();
        invalidMention["definition"] = new JsonObject { ["trigger"] = "WorkItemCreated", ["conditions"] = new JsonArray(),
            ["actions"] = new JsonArray(Action("AddComment", "Hello @{" + Text(outsider, "id") + "}")) };
        var mentionError = await session.CallAsync("Reject inaccessible comment mention before enablement", "POST", workflows + "/validate", invalidMention, token, 400);
        session.State("Mention validation returns field errors", mentionError["errors"]!.AsObject().ContainsKey("definition.actions[0]"));
        var workflow = await session.CallAsync("Create workflow", "POST", workflows, body, token, 201);
        var wid = workflows + "/" + Text(workflow, "id");
        session.State("Created workflow starts disabled", !workflow["isEnabled"]!.GetValue<bool>());
        await session.CallAsync("Read workflow", "GET", wid, token: token);
        await session.CallAsync("Hide workflow from outsider", "GET", wid, token: outsiderToken, expected: 404);
        var stateError = await session.CallAsync("Reject invalid state version", "POST", wid + "/enable", new { version = 0 }, token, 400);
        session.State("State validation returns version field error", stateError["errors"]!.AsObject().ContainsKey("version"));
        var listing = await session.CallAsync("List workflows", "GET", workflows, token: token);
        session.State("List contains created workflow", listing["total"]!.GetValue<int>() == 1);
        await session.CallAsync("Enable workflow", "POST", wid + "/enable", new { version = 1 }, token);
        var itemBody = new { title = "Critical live item", type = "Task", status = "Todo", priority = "Critical" };
        var item = await session.CallAsync("Create triggering work item", "POST", pp + "/work-items", itemBody, token, 201);
        await session.WaitForExecutionAsync(wid, 1, token);
        var history = await session.CallAsync("Read execution history", "GET", wid + "/executions", token: token);
        session.State("Hosted worker completes workflow", history["total"]!.GetValue<int>() == 1);
        var execution = await session.CallAsync("Execution details", "GET", wid + "/executions/" + Text(history["items"]![0]!, "id"), token: token);
        var executionPath = workflows + "/executions/" + Text(execution, "id");
        var lookup = await session.CallAsync("Look up execution from activity", "GET", executionPath, token: token);
        session.State("Activity lookup returns the same execution details", JsonNode.DeepEquals(execution, lookup));
        await session.CallAsync("Hide execution lookup from developer", "GET", executionPath, token: developerToken, expected: 404);
        await session.CallAsync("Hide execution lookup from outsider", "GET", executionPath, token: outsiderToken, expected: 404);
        session.State("Execution exposes pinned version and definition", execution["version"]!.GetValue<int>() == 1 && Text(execution["definition"]!, "trigger") == "WorkItemCreated");
        session.State("Execution exposes original actor and source metadata", Text(execution, "actorUserId") == Text(owner, "id") &&
            !string.IsNullOrEmpty(Text(execution, "correlationId")) && execution["originExecutionId"] == null && execution["depth"]!.GetValue<int>() == 0);
        await session.CallAsync("Hide execution history from developer", "GET", wid + "/executions", token: developerToken, expected: 404);
        session.State("All ordered actions succeeded", execution["actions"]!.AsArray().Count == 4 && execution["actions"]!.AsArray().All(a => Text(a!, "status") == "Succeeded"));
        var current = await session.CallAsync("Read automated item", "GET", pp + "/work-items/" + Text(item, "id"), token: token);
        session.State("Automation preserves item mutations", Text(current, "priority") == "High" && Text(current, "status") == "InProgress" && current["version"]!.GetValue<long>() == 3);
        var comments = await session.CallAsync("Read automated comment", "GET", pp + "/work-items/" + Text(item, "id") + "/comments", token: token);
        session.State("Automated comment persisted once", comments["total"]!.GetValue<int>() == 1 && Text(comments["items"]![0]!, "body") == "Live workflow completed.");
        var activity = await session.CallAsync("Read activity", "GET", wp + "/activity?projectId=" + Text(project, "id") + "&workItemId=" + Text(item, "id"), token: token);
        session.State("Audit exposes execution linkage", activity["items"]!.AsArray().Any(a => a!["workflowExecutionId"]?.GetValue<string>() == Text(execution, "id")));
        JsonNode? notices = null;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            notices = await session.CallAsync("Poll workflow notification", "GET", "/api/v1/notifications", token: token, record: false);
            if (notices["items"]!.AsArray().Any(n => Text(n!, "type") == "Workflow")) break;
            await Task.Delay(200);
        }
        session.State("Workflow notification persisted", notices != null && notices["items"]!.AsArray().Any(n => Text(n!, "type") == "Workflow" && Text(n!, "workItemId") == Text(item, "id")));
        var newDefinition = definition.DeepClone(); newDefinition["actions"]![2]!["value"] = "Live workflow version two completed.";
        var newBody = body.DeepClone(); newBody["name"] = "Critical work v2"; newBody["definition"] = newDefinition; newBody["version"] = 1;
        var updated = await session.CallAsync("Update workflow creates version", "PUT", wid, newBody, token);
        var oldAfterEdit = await session.CallAsync("Read historical execution after edit", "GET", wid + "/executions/" + Text(execution, "id"), token: token);
        session.State("Historical definition remains inspectable after edit", oldAfterEdit["version"]!.GetValue<int>() == 1 && Text(oldAfterEdit["definition"]!["actions"]![2]!, "value") == "Live workflow completed.");
        session.State("Configuration version increments", updated["version"]!.GetValue<int>() == 2);
        var stale = body.DeepClone(); stale["version"] = 1;
        await session.CallAsync("Reject stale update", "PUT", wid, stale, token, 409);
        await session.CallAsync("Reject stale enable", "POST", wid + "/enable", new { version = 1 }, token, 409);
        await session.CallAsync("Disable workflow", "POST", wid + "/disable", new { version = 2 }, token);
        await session.CallAsync("Create item while disabled", "POST", pp + "/work-items", itemBody, token, 201);
        await session.CallAsync("Reenable workflow", "POST", wid + "/enable", new { version = 2 }, token);
        var freshItem = await session.CallAsync("Create item after reenable", "POST", pp + "/work-items", itemBody, token, 201);
        history = await session.WaitForExecutionAsync(wid, 2, token);
        session.State("Disabled events are not replayed", history["items"]!.AsArray().Select(e => Text(e!, "workItemId")).ToHashSet().SetEquals([Text(item, "id"), Text(freshItem, "id")]));
        history = await session.CallAsync("Read execution history after new version", "GET", wid + "/executions", token: token);
        session.State("Execution pins version", history["items"]!.AsArray().Select(e => Text(e!, "workflowVersionId")).Distinct().Count() == 2);
        var byVersion = history["items"]!.AsArray().ToDictionary(e => e!["version"]!.GetValue<int>(), e => e!);
        session.State("History returns both immutable definitions", Text(byVersion[1]["definition"]!["actions"]![2]!, "value") == "Live workflow completed." &&
            Text(byVersion[2]["definition"]!["actions"]![2]!, "value") == "Live workflow version two completed.");
        await session.CallAsync("Reject invalid pagination", "GET", wid + "/executions?pageSize=101", token: token, expected: 400);
        await session.CallAsync("Reject missing execution", "GET", wid + "/executions/" + Guid.NewGuid(), token: token, expected: 404);
        await session.CallAsync("Disable after audit", "POST", wid + "/disable", new { version = 2 }, token);
        await session.CallAsync("Reject invalid disable version", "POST", wid + "/disable", new { version = 0 }, token, 400);
        var invalidUpdate = bad.DeepClone(); invalidUpdate["version"] = 2;
        await session.CallAsync("Reject invalid workflow update", "PUT", wid, invalidUpdate, token, 400);
        await session.CallAsync("Hide nonexistent workflow", "GET", workflows + "/" + Guid.NewGuid(), token: token, expected: 404);
        await session.CallAsync("Archive project", "POST", pp + "/archive", token: token);
        await session.CallAsync("Reject workflow enable on archived project", "POST", wid + "/enable", new { version = 2 }, token, 409);
        await session.CallAsync("Reject workflow creation on archived project", "POST", workflows, body, token, 409);
        var methods = new[] { "get", "post", "put", "patch", "delete" };
        var operations = contract["paths"]!.AsObject().Where(p => p.Key.Contains("/workflows", StringComparison.Ordinal))
            .SelectMany(p => methods.Where(m => p.Value!.AsObject().ContainsKey(m)).Select(m => (m.ToUpperInvariant(), p.Key.TrimEnd('/')))).ToHashSet();
        var exercised = session.Requests.Where(r => (int)r.Actual is >= 200 and < 300)
            .Select(r => (r.Method!, r.Path!.Split('?')[0].TrimEnd('/'))).ToHashSet();
        session.State("All ten workflow OpenAPI operations succeed over HTTP", operations.Count == 10 && operations.IsSubsetOf(exercised));
    }
    private Task<JsonNode> RegisterAsync(string name, string prefix, string displayName) =>
        session.CallAsync(name, "POST", "/api/v1/auth/register", new { email = prefix + Guid.NewGuid().ToString("N") + "@example.com",
            password = "PhaseFive!Secure123", displayName }, expected: 201);
    private static string Text(JsonNode value, string field) => value[field]!.GetValue<string>();
    private static JsonObject Action(string type, string? value = null, string? userId = null)
    {
        var action = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["type"] = type };
        if (value != null) action["value"] = value;
        if (userId != null) action["userId"] = userId;
        return action;
    }
}
