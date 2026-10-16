using System.ComponentModel.DataAnnotations;
using FlowForge.Domain.Workspaces;

namespace FlowForge.Api.Validation;

internal static class InputRules
{
    public static void Email(string? email, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !new EmailAddressAttribute().IsValid(email.Trim()))
            errors["email"] = ["A valid email address of at most 320 characters is required."];
    }
    public static void Name(string? name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            errors["name"] = ["Name must be between 1 and 100 characters."];
    }
    public static void Role(string? role, Dictionary<string, string[]> errors)
    {
        if (role == null || !Enum.GetNames<WorkspaceRole>().Any(name => string.Equals(name, role, StringComparison.OrdinalIgnoreCase)))
            errors["role"] = ["A valid workspace role is required."];
    }
    public static void Id(Guid id, string field, Dictionary<string, string[]> errors)
    {
        if (id == Guid.Empty) errors[field] = ["A non-empty user ID is required."];
    }
    public static void Token(string? token, string field, Dictionary<string, string[]> errors, bool allowEmpty = false)
    {
        if (allowEmpty && string.IsNullOrWhiteSpace(token)) return;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            errors[field] = ["A token of at most 256 characters is required."];
    }
}
