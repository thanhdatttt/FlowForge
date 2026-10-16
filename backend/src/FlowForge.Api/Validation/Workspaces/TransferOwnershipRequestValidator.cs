using FlowForge.Api.Contracts.Workspaces;

namespace FlowForge.Api.Validation.Workspaces;

public static class TransferOwnershipRequestValidator
{
    public static Dictionary<string, string[]> Validate(TransferOwnershipRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Id(request.NewOwnerUserId, "newOwnerUserId", errors);
        return errors;
    }
}
