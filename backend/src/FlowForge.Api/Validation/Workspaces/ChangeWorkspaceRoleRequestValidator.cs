using FlowForge.Api.Contracts.Workspaces;

namespace FlowForge.Api.Validation.Workspaces;

public static class ChangeWorkspaceRoleRequestValidator
{
    public static Dictionary<string, string[]> Validate(ChangeWorkspaceRoleRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Role(request.Role, errors);
        return errors;
    }
}
