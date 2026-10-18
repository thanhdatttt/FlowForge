using System.Globalization;
using FlowForge.Domain.Workflows;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFiveRulesTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly WorkflowConditionContext Context = new("High", "Todo", "Task", null, Actor,
        ["bug", "urgent"], new DateOnly(2026, 10, 18), "Example", null);

    [Theory]
    [InlineData("Priority", "Equals", "High", true)]
    [InlineData("Priority", "NotEquals", "High", false)]
    [InlineData("Priority", "GreaterThan", "Medium", true)]
    [InlineData("Priority", "LessThan", "Critical", true)]
    [InlineData("Priority", "GreaterThanOrEqual", "High", true)]
    [InlineData("Priority", "LessThanOrEqual", "Medium", false)]
    [InlineData("DueDate", "LessThan", "2026-10-19", true)]
    [InlineData("DueDate", "GreaterThan", "2026-10-19", false)]
    [InlineData("DueDate", "Equals", "2026-10-18", true)]
    [InlineData("Label", "Equals", "urgent", true)]
    [InlineData("Label", "NotEquals", "bug", false)]
    [InlineData("Status", "Equals", "Todo", true)]
    [InlineData("Type", "Equals", "Issue", false)]
    [InlineData("Actor", "Equals", "11111111-1111-1111-1111-111111111111", true)]
    [InlineData("Title", "Equals", "Example", true)]
    public void Conditions_use_typed_comparisons(string field, string op, string value, bool expected) =>
        Assert.Equal(expected, WorkflowConditions.Matches(new(field, op, [value]), Context));

    [Theory]
    [InlineData("Assignee", "IsEmpty", true)]
    [InlineData("Assignee", "IsNotEmpty", false)]
    [InlineData("Description", "IsEmpty", true)]
    [InlineData("Label", "IsEmpty", false)]
    public void Empty_conditions_handle_nullable_and_collection_fields(string field, string op, bool expected) =>
        Assert.Equal(expected, WorkflowConditions.Matches(new(field, op, []), Context));

    [Fact]
    public void In_matches_any_allowlisted_value() => Assert.True(WorkflowConditions.Matches(new("Priority", "In", ["Low", "High"]), Context));

    [Theory]
    [InlineData("Priority", "GreaterThan", "Urgent")]
    [InlineData("Status", "GreaterThan", "Todo")]
    [InlineData("Actor", "Equals", "not-a-user")]
    [InlineData("Assignee", "Equals", "00000000-0000-0000-0000-000000000000")]
    [InlineData("DueDate", "Equals", "tomorrow")]
    [InlineData("Type", "Equals", "Epic")]
    [InlineData("Script", "Equals", "code")]
    [InlineData("Priority", "Execute", "High")]
    public void Invalid_condition_configurations_are_rejected(string field, string op, string value) =>
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(new("WorkItemCreated", [new(field, op, [value])], [new(Guid.NewGuid(), "Unassign")])));

    [Fact]
    public void Null_collections_duplicate_action_ids_and_unexpected_parameters_are_rejected()
    {
        var id = Guid.NewGuid();
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(null));
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(new("WorkItemCreated", null, null)));
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(new("WorkItemCreated", [], [new(id, "Unassign"), new(id, "Unassign")])));
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(new("WorkItemCreated", [], [new(id, "Unassign", Guid.NewGuid())])));
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(new("WorkItemCreated", [new("Priority", "Equals", [])], [new(id, "Unassign")])));
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(new("WorkItemCreated", [], [new(id, "AddComment", Value: " ")])));
        Assert.NotEmpty(WorkflowDefinitionRules.Validate(new("InvitationCreated", [], [new(id, "Unassign")])));
    }
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void Iso_date_conditions_use_gregorian_dates_independently_of_host_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var condition = new WorkflowCondition("DueDate", "Equals", ["2026-10-18"]);
            Assert.Empty(WorkflowDefinitionRules.Validate(new("WorkItemCreated", [condition], [new(Guid.NewGuid(), "Unassign")])));
            Assert.True(WorkflowConditions.Matches(condition, Context));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

}
