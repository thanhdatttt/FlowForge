using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FlowForge.Application.Ai;

public static partial class AiOutputRules
{
    public const int MaxOutputCharacters = 131072;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16,
        PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false
    };

    public static T Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxOutputCharacters)
            throw new AiProviderException(AiProviderFailure.InvalidResponse);
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new AiProviderException(AiProviderFailure.InvalidResponse); }
        catch (JsonException) { throw new AiProviderException(AiProviderFailure.InvalidResponse); }
    }

    public static Dictionary<string, string[]> ValidateTasks(IReadOnlyList<AiTaskProposal>? tasks)
    {
        var errors = new Dictionary<string, string[]>();
        if (tasks == null || tasks.Count is < 1 or > 20)
        { errors["tasks"] = ["Supply 1–20 task proposals."]; return errors; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            var field = $"tasks[{i}]";
            if (task == null) { errors[field] = ["A task is required."]; continue; }
            if (task.Id == null || !ProposalId().IsMatch(task.Id) || !ids.Add(task.Id))
                errors[field + ".id"] = ["Supply a unique 1–40 character ID starting with a letter (letters, digits, _ and - only)."];
            if (string.IsNullOrWhiteSpace(task.Title) || task.Title.Trim().Length > 200)
                errors[field + ".title"] = ["Title must be 1–200 characters."];
            if (task.Description?.Length > 20000) errors[field + ".description"] = ["Description must be at most 20000 characters."];
            if (task.Priority is not ("Low" or "Medium" or "High" or "Critical")) errors[field + ".priority"] = ["Invalid priority."];
            if (task.DependsOn == null || task.DependsOn.Count > 19 || task.DependsOn.Any(string.IsNullOrWhiteSpace) ||
                task.DependsOn.Distinct(StringComparer.Ordinal).Count() != task.DependsOn.Count)
                errors[field + ".dependsOn"] = ["Supply up to 19 unique dependency IDs, or an empty array."];
        }
        if (errors.Count != 0) return errors;
        var byId = tasks.ToDictionary(x => x.Id!, StringComparer.Ordinal);
        for (var i = 0; i < tasks.Count; i++)
            if (tasks[i].DependsOn!.Any(id => id == tasks[i].Id || !byId.ContainsKey(id)))
                errors[$"tasks[{i}].dependsOn"] = ["Dependencies must reference other tasks in this selection."];
        if (errors.Count != 0) return errors;
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Cycle(string id)
        {
            if (visited.Contains(id)) return false;
            if (!visiting.Add(id)) return true;
            foreach (var dependency in byId[id].DependsOn!) if (Cycle(dependency)) return true;
            visiting.Remove(id); visited.Add(id); return false;
        }
        if (byId.Keys.Any(Cycle)) errors["tasks"] = ["Dependency cycles are not allowed."];
        return errors;
    }

    public static bool ValidNarrative(AiProjectNarrative value) =>
        new[] { value.Overview, value.CompletedWork, value.Blockers, value.OverdueWork }
            .All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 4000) &&
        value.Risks is { Count: <= 10 } && value.Risks.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 1000);

    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProposalId();
}
