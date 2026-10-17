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

        var passwordErrors = new List<string>();
        var password = request.Password;
        if (password is null || password.Length < 8 || password.Length > 128)
            passwordErrors.Add("Password must be between 8 and 128 characters.");
        if (!string.IsNullOrEmpty(password))
        {
            if (!password.Any(char.IsUpper)) passwordErrors.Add("Password must contain an uppercase letter.");
            if (!password.Any(char.IsLower)) passwordErrors.Add("Password must contain a lowercase letter.");
            if (!password.Any(char.IsDigit)) passwordErrors.Add("Password must contain a digit.");
            if (!password.Any(character => char.IsPunctuation(character) || char.IsSymbol(character)))
                passwordErrors.Add("Password must contain a special character.");
        }
        if (passwordErrors.Count > 0) errors["password"] = passwordErrors.ToArray();

        return errors;
    }
}
