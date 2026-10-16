using FlowForge.Api.Contracts.Workspaces;

namespace FlowForge.Api.Validation.Workspaces;

public static class UpdateWorkspaceRequestValidator
{
    public static Dictionary<string, string[]> Validate(UpdateWorkspaceRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Name(request.Name, errors);
        return errors;
    }
}
