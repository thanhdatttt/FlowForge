using FlowForge.Api.Contracts.Identity;

namespace FlowForge.Api.Validations.Identity;

public static class RegisterRequestValidator
{
    public static Dictionary<string, string[]> Validate(RegisterRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Email(request.Email, errors);
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 100)
            errors["displayName"] = ["Display name must be between 1 and 100 characters."];

        PasswordRules.Validate(request.Password, errors);

        return errors;
    }
}
