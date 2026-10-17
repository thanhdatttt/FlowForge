using FlowForge.Api.Contracts.WorkItems;

namespace FlowForge.Api.Validation.WorkItems;

public static class TimeEntryWriteRequestValidator
{
    public static Dictionary<string, string[]> Validate(TimeEntryWriteDto request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Minutes < 1 || request.Minutes > 1000000) errors["minutes"] = ["Minutes must be between 1 and 1000000."];
        if (request.Note?.Length > 2000) errors["note"] = ["Note must be at most 2000 characters."];
        return errors;
    }
}
