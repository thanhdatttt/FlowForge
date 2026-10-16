using FlowForge.Api.Contracts.Identity;

namespace FlowForge.Api.Validation.Identity;

public static class RefreshTokenRequestValidator
{
    public static Dictionary<string, string[]> Validate(RefreshTokenRequestDto request, bool allowEmpty = false)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Token(request.RefreshToken, "refreshToken", errors, allowEmpty);
        return errors;
    }
}
