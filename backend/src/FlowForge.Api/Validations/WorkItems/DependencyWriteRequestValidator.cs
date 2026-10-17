using FlowForge.Api.Contracts.WorkItems;

namespace FlowForge.Api.Validations.WorkItems;

public static class DependencyWriteRequestValidator
{
    public static Dictionary<string, string[]> Validate(DependencyWriteDto request, Guid itemId)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.DependsOnItemId == itemId) errors["dependsOnItemId"] = ["Self-dependency is not allowed."];
        return errors;
    }
}
