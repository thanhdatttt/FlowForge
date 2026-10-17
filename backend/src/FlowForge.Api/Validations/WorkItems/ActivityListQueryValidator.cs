namespace FlowForge.Api.Validations.WorkItems;

public static class ActivityListQueryValidator
{
    public static Dictionary<string, string[]> Validate(Guid? projectId, Guid? workItemId, int? page, int? pageSize)
    {
        var errors = PaginationValidator.Validate(page, pageSize);
        if (workItemId != null && projectId == null)
            errors["projectId"] = ["Project ID is required when filtering by work item."];
        return errors;
    }
}
