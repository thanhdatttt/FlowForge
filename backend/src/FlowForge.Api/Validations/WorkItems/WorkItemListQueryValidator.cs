namespace FlowForge.Api.Validations.WorkItems;

public static class WorkItemListQueryValidator
{
    public static Dictionary<string, string[]> Validate(string? search, string? status, string? priority,
        string? type, DateOnly? dueBefore, DateOnly? dueAfter, string? sort, int? page, int? pageSize)
    {
        var errors = PaginationValidator.Validate(page, pageSize);
        if (search?.Length > 200) errors["search"] = ["Search must be at most 200 characters."];
        if (sort is not null && sort is not ("createdAt" or "updatedAt" or "dueDate" or "priority" or "key"))
            errors["sort"] = ["Invalid sort field."];
        if (status is not null && (string.IsNullOrWhiteSpace(status) || status.Length > 40)) errors["status"] = ["Invalid status."];
        if (priority is not null && priority is not ("Low" or "Medium" or "High" or "Critical")) errors["priority"] = ["Invalid priority."];
        if (type is not null && type is not ("Task" or "Issue")) errors["type"] = ["Invalid type."];
        if (dueBefore != null && dueAfter != null && dueAfter > dueBefore)
            errors["dueAfter"] = ["Due date range is invalid."];
        return errors;
    }
}
