using FlowForge.Api.Contracts.Workspaces;

namespace FlowForge.Api.Validations.Workspaces;

public static class CreateInvitationRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateInvitationRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Email(request.Email, errors);
        InputRules.Role(request.Role, errors);
        return errors;
    }
}
