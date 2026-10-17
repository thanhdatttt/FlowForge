using FlowForge.Api.Contracts.WorkItems;

namespace FlowForge.Api.Validation.WorkItems;

public static class CommentWriteRequestValidator
{
    public static Dictionary<string, string[]> Validate(CommentWriteDto request, bool update)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 10000)
            errors["body"] = ["Body must be 1–10000 characters."];
        if (update && request.Version is null or < 1) errors["version"] = ["Version is required."];
        return errors;
    }
}
