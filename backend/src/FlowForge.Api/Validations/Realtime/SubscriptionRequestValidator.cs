namespace FlowForge.Api.Validations.Realtime;

public static class SubscriptionRequestValidator
{
    public static Dictionary<string, string[]> Validate(Guid? workspaceId = null, Guid? projectId = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (workspaceId == Guid.Empty) errors["workspaceId"] = ["A non-empty workspace ID is required."];
        if (projectId == Guid.Empty) errors["projectId"] = ["A non-empty project ID is required."];
        return errors;
    }
}
