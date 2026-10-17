using FlowForge.Api.Contracts.Identity;

namespace FlowForge.Api.Validation.Identity;

public static class EmailVerificationRequestValidator
{
    public static Dictionary<string, string[]> Validate(EmailVerificationRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Code is not { Length: 6 } || !request.Code.All(character => character is >= '0' and <= '9'))
            errors["code"] = ["Enter a six-digit verification code."];
        return errors;
    }
}
