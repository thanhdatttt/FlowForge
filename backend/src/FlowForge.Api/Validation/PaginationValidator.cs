namespace FlowForge.Api.Validation;

public static class PaginationValidator
{
    public static Dictionary<string, string[]> Validate(int? page, int? pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page is < 1 or > 1000000) errors["page"] = ["Page must be between 1 and 1000000."];
        if (pageSize is < 1 or > 100) errors["pageSize"] = ["Page size must be between 1 and 100."];
        return errors;
    }
    public static (int Number, int Size) Defaults(int? page, int? pageSize) => (page ?? 1, pageSize ?? 20);
}
