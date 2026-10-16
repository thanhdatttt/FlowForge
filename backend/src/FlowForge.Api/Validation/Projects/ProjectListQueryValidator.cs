namespace FlowForge.Api.Validation.Projects;

public static class ProjectListQueryValidator
{
    public static Dictionary<string, string[]> Validate(string? search, string? sort, Guid? leadUserId, int? page, int? pageSize)
    {
        var errors = PaginationValidator.Validate(page, pageSize);
        if (search is { Length: > 100 }) errors["search"] = ["Search must be at most 100 characters."];
        if (sort != null && sort is not ("name" or "key" or "createdAt" or "updatedAt"))
            errors["sort"] = ["Sort must be name, key, createdAt, or updatedAt."];
        if (leadUserId != null) InputRules.Id(leadUserId.Value, "leadUserId", errors);
        return errors;
    }
}
