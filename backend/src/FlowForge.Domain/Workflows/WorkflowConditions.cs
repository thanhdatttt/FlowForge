using System.Globalization;

namespace FlowForge.Domain.Workflows;

public static class WorkflowConditions
{
    public static bool Matches(WorkflowCondition condition, WorkflowConditionContext context)
    {
        string[] actual = condition.Field switch
        {
            "Priority" => [context.Priority], "Status" => [context.Status], "Type" => [context.Type],
            "Assignee" => context.Assignee is Guid assignee ? [assignee.ToString()] : [],
            "Actor" => context.Actor is Guid actor ? [actor.ToString()] : [],
            "Label" => context.Labels.ToArray(),
            "DueDate" => context.DueDate is DateOnly due ? [due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] : [],
            "Title" => [context.Title], "Description" => string.IsNullOrEmpty(context.Description) ? [] : [context.Description],
            _ => []
        };
        if (condition.Operator == "IsEmpty") return actual.Length == 0;
        if (condition.Operator == "IsNotEmpty") return actual.Length != 0;
        var values = condition.Values!;
        bool Equal(string a, string b) => condition.Field is "Assignee" or "Actor"
            ? Guid.Parse(a) == Guid.Parse(b) : string.Equals(a, b, StringComparison.Ordinal);
        var equal = actual.Any(a => values.Any(v => Equal(a, v)));
        if (condition.Operator is "Equals" or "In") return equal;
        if (condition.Operator == "NotEquals") return !equal;
        if (actual.Length == 0) return false;
        var comparison = condition.Field == "Priority"
            ? Array.IndexOf(WorkflowDefinitionRules.Priorities, actual[0]).CompareTo(Array.IndexOf(WorkflowDefinitionRules.Priorities, values[0]))
            : string.CompareOrdinal(actual[0], values[0]);
        return condition.Operator switch
        {
            "GreaterThan" => comparison > 0, "LessThan" => comparison < 0,
            "GreaterThanOrEqual" => comparison >= 0, "LessThanOrEqual" => comparison <= 0, _ => false
        };
    }
}
