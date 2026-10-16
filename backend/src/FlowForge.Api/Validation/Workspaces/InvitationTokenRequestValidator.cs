using FlowForge.Api.Contracts.Workspaces;

namespace FlowForge.Api.Validation.Workspaces;

public static class InvitationTokenRequestValidator
{
    public static Dictionary<string, string[]> Validate(InvitationTokenRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Token(request.Token, "token", errors);
        return errors;
    }
}
