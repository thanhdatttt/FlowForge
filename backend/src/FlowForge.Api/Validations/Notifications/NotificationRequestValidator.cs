namespace FlowForge.Api.Validations.Notifications;

public static class NotificationRequestValidator
{
    public static Dictionary<string, string[]> Validate(Guid notificationId)
    {
        var errors = new Dictionary<string, string[]>();
        if (notificationId == Guid.Empty) errors["notificationId"] = ["A non-empty notification ID is required."];
        return errors;
    }
}
