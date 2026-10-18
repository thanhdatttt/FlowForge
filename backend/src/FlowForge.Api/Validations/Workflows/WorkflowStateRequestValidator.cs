using FlowForge.Api.Contracts.Workflows;

namespace FlowForge.Api.Validations.Workflows;

public static class WorkflowStateRequestValidator
{
    public static Dictionary<string, string[]> Validate(WorkflowStateDto request) => request.Version < 1
        ? new() { ["version"] = ["Version is required."] } : [];
}
