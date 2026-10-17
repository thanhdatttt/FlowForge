using FlowForge.Api.Contracts.Workspaces;

namespace FlowForge.Api.Validations.Workspaces;

public static class AddWorkspaceMemberRequestValidator
{
    public static Dictionary<string, string[]> Validate(AddWorkspaceMemberRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Email(request.Email, errors);
        InputRules.Role(request.Role, errors);
        return errors;
    }
}
