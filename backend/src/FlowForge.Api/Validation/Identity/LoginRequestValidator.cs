using FlowForge.Api.Contracts.Identity;

namespace FlowForge.Api.Validation.Identity;

public static class LoginRequestValidator
{
    public static Dictionary<string, string[]> Validate(LoginRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Email(request.Email, errors);
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            errors["password"] = ["A password of at most 128 characters is required."];
        return errors;
    }
}
