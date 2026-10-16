using FlowForge.Api.Contracts.Workspaces;

namespace FlowForge.Api.Validation.Workspaces;

public static class CreateWorkspaceRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateWorkspaceRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Name(request.Name, errors);
        return errors;
    }
}
