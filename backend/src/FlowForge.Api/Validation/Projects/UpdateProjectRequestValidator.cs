using FlowForge.Api.Contracts.Projects;

namespace FlowForge.Api.Validation.Projects;

public static class UpdateProjectRequestValidator
{
    public static Dictionary<string, string[]> Validate(UpdateProjectRequestDto request)
    {
        var errors = ProjectFieldsValidator.Validate(request.Name, request.ProjectKey, request.Description,
            request.LeadUserId, request.StartsOn, request.TargetOn);
        if (request.Version < 1) errors["version"] = ["Version must be a positive number."];
        return errors;
    }
}
