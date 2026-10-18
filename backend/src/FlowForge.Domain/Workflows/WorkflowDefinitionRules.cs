using System.Globalization;

namespace FlowForge.Domain.Workflows;

public static class WorkflowDefinitionRules
{
    public static readonly string[] Triggers = ["WorkItemCreated", "WorkItemUpdated", "WorkItemStatusChanged",
        "WorkItemAssignmentChanged", "CommentAdded", "WorkItemDueDateReached", "WorkItemDueDateOverdue"];
    public static readonly string[] Priorities = ["Low", "Medium", "High", "Critical"];
    public static Dictionary<string, string[]> Validate(WorkflowDefinition? definition)
    {
        var errors = new Dictionary<string, string[]>();
        if (definition == null) { errors["definition"] = ["Definition is required."]; return errors; }
        if (!Triggers.Contains(definition.Trigger)) errors["definition.trigger"] = ["Unsupported trigger."];
        if (definition.Conditions == null || definition.Conditions.Count > 20)
            errors["definition.conditions"] = ["Supply up to 20 ALL conditions."];
        else for (var i = 0; i < definition.Conditions.Count; i++)
        {
            var c = definition.Conditions[i];
            var field = $"definition.conditions[{i}]";
            if (c == null || !new[] { "Priority", "Status", "Type", "Assignee", "Actor", "Label", "DueDate", "Title", "Description" }.Contains(c.Field))
            { errors[field] = ["Unsupported field."]; continue; }
            var empty = c.Operator is "IsEmpty" or "IsNotEmpty";
            var comparison = c.Operator is "GreaterThan" or "LessThan" or "GreaterThanOrEqual" or "LessThanOrEqual";
            if (!new[] { "Equals", "NotEquals", "In", "IsEmpty", "IsNotEmpty", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual" }.Contains(c.Operator) ||
                comparison && c.Field is not ("Priority" or "DueDate") || c.Values == null ||
                (empty ? c.Values.Count != 0 : c.Values.Count is < 1 or > 50 || c.Operator != "In" && c.Values.Count != 1))
            { errors[field] = ["Invalid operator or value count."]; continue; }
            if (c.Values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 200 ||
                c.Field == "Priority" && !Priorities.Contains(v) || c.Field == "Type" && v is not ("Task" or "Issue") ||
                c.Field is "Actor" or "Assignee" && (!Guid.TryParse(v, out var id) || id == Guid.Empty) ||
                c.Field == "DueDate" && !DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                errors[field] = ["Invalid typed condition value."];
        }
        if (definition.Actions == null || definition.Actions.Count is < 1 or > 20)
            errors["definition.actions"] = ["Supply 1–20 ordered actions."];
        else for (var i = 0; i < definition.Actions.Count; i++)
        {
            var a = definition.Actions[i];
            var field = $"definition.actions[{i}]";
            if (a == null || a.Id == Guid.Empty || definition.Actions.Count(x => x?.Id == a.Id) != 1 ||
                !new[] { "Assign", "Unassign", "ChangeStatus", "ChangePriority", "AddLabel", "RemoveLabel", "AddComment", "Notify", "SendEmail" }.Contains(a.Type))
            { errors[field] = ["A unique action ID and supported type are required."]; continue; }
            var recipient = a.Type is "Assign" or "Notify" or "SendEmail";
            var value = a.Type is "ChangeStatus" or "ChangePriority" or "AddLabel" or "RemoveLabel" or "AddComment";
            var limit = a.Type == "AddComment" ? 10000 : a.Type is "AddLabel" or "RemoveLabel" ? 50 : 40;
            if (recipient ? a.UserId is null || a.UserId == Guid.Empty : a.UserId != null)
                errors[field] = ["Only recipient actions require a non-empty user ID."];
            if (value ? string.IsNullOrWhiteSpace(a.Value) || a.Value.Trim().Length > limit : a.Value != null)
                errors[field] = ["Invalid action value."];
            if (a.Type == "ChangePriority" && !Priorities.Contains(a.Value)) errors[field] = ["Invalid priority."];
        }
        return errors;
    }
}
