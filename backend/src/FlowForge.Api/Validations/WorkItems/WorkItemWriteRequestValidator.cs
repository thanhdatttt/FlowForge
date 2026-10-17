using FlowForge.Api.Contracts.WorkItems;

namespace FlowForge.Api.Validations.WorkItems;

public static class WorkItemWriteRequestValidator
{
    public static Dictionary<string, string[]> Validate(WorkItemWriteDto request, bool update)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200) errors["title"] = ["Title must be 1–200 characters."];
        if (request.Description?.Length > 20000) errors["description"] = ["Description must be at most 20000 characters."];
        if (request.Type is not ("Task" or "Issue")) errors["type"] = ["Type must be Task or Issue."];
        if (string.IsNullOrWhiteSpace(request.Status) || request.Status.Length > 40) errors["status"] = ["Status must be 1–40 characters."];
        if (request.Priority is not ("Low" or "Medium" or "High" or "Critical")) errors["priority"] = ["Invalid priority."];
        if (request.EstimateMinutes is < 0) errors["estimateMinutes"] = ["Estimate cannot be negative."];
        if (request.StartDate > request.DueDate) errors["dueDate"] = ["Due date cannot precede start date."];
        if (!ValidLabelIds(request.LabelIds)) errors["labelIds"] = ["Labels must be unique and at most 20."];
        if (update && request.Version is null or < 1) errors["version"] = ["Version is required."];
        return errors;
    }

    public static bool ValidLabelIds(IReadOnlyList<Guid>? labels) =>
        labels == null || labels.Count <= 20 && labels.Distinct().Count() == labels.Count;
}
