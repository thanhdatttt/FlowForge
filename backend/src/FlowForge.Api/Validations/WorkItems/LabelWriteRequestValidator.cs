using FlowForge.Api.Contracts.WorkItems;
using System.Text.RegularExpressions;

namespace FlowForge.Api.Validations.WorkItems;

public static class LabelWriteRequestValidator
{
    public static Dictionary<string, string[]> Validate(LabelWriteDto request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 50) errors["name"] = ["Name must be 1–50 characters."];
        if (request.Color is not null && !Regex.IsMatch(request.Color, "^#[0-9a-fA-F]{6}$"))
            errors["color"] = ["Color must be a six-digit hex value."];
        return errors;
    }
}
