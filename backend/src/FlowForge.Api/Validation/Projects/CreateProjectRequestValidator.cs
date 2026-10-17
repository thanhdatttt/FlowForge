using FlowForge.Api.Contracts.Projects;

namespace FlowForge.Api.Validation.Projects;

public static class CreateProjectRequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateProjectRequestDto request)
    {
        var errors = ProjectFieldsValidator.Validate(request.Name, request.ProjectKey, request.Description,
            request.LeadUserId, request.StartsOn, request.TargetOn);
        return errors;
    }
}
