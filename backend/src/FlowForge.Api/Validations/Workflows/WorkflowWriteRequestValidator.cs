using FlowForge.Api.Contracts.Workflows;
using FlowForge.Domain.Workflows;

namespace FlowForge.Api.Validations.Workflows;

public static class WorkflowWriteRequestValidator
{
    public static Dictionary<string, string[]> Validate(WorkflowWriteDto request, bool update)
    {
        var errors = WorkflowDefinitionRules.Validate(request.Definition);
        InputRules.Name(request.Name, errors);
        if (request.ProjectId == Guid.Empty) errors["projectId"] = ["A non-empty project ID is required."];
        if (update && request.Version is null or < 1) errors["version"] = ["Version is required."];
        return errors;
    }
}
