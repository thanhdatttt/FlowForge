using System.Text.RegularExpressions;

namespace FlowForge.Api.Validation.Projects;

internal static class ProjectFieldsValidator
{
    public static Dictionary<string, string[]> Validate(string? name, string? key, string? description,
        Guid? leadUserId, DateOnly? start, DateOnly? target)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Name(name, errors);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 20 || !Regex.IsMatch(key.Trim().ToUpperInvariant(), "^[A-Z][A-Z0-9]{1,9}$"))
            errors["projectKey"] = ["Project key must be 2–10 letters or digits, starting with a letter."];
        if (description is { Length: > 4000 }) errors["description"] = ["Description must be at most 4000 characters."];
        if (leadUserId != null) InputRules.Id(leadUserId.Value, "leadUserId", errors);
        if (start != null && target != null && target < start) errors["targetOn"] = ["Target date must not precede start date."];
        return errors;
    }
}
