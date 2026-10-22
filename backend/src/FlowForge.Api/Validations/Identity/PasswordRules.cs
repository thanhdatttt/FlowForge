namespace FlowForge.Api.Validations.Identity;

public static class PasswordRules
{
    public static void Validate(string? password, Dictionary<string, string[]> errors)
    {
        var passwordErrors = new List<string>();
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
    }
}
