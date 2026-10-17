using FlowForge.Api.Contracts.Projects;

namespace FlowForge.Api.Validations.Projects;

public static class ProjectMemberRequestValidator
{
    public static Dictionary<string, string[]> Validate(ProjectMemberRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        InputRules.Id(request.UserId, "userId", errors);
        return errors;
    }
}
