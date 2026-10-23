using FlowForge.Application.Ai;
using FlowForge.Api.Contracts.Ai;
using FlowForge.Api.Validations.Ai;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseSixAiRulesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"tasks\":\"invalid\"}")]
    [InlineData("{\"tasks\":[],\"script\":\"run\"}")]
    [InlineData("{\"tasks\":[],\"tasks\":[]}")]
    [InlineData("{\"tasks\":[{\"id\":\"base\",\"commands\":[]}]}")]
    public void Malformed_or_unexpected_provider_output_is_rejected(string output)
    {
        Assert.Equal(AiProviderFailure.InvalidResponse, Assert.Throws<AiProviderException>(() => AiOutputRules.Parse<AiTaskGenerationResult>(output)).Failure);
    }

    [Fact]
    public void Oversized_output_is_rejected_before_deserialization() =>
        Assert.Throws<AiProviderException>(() => AiOutputRules.Parse<AiTaskGenerationResult>(new string('x', AiOutputRules.MaxOutputCharacters + 1)));

    [Fact]
    public void Valid_proposals_and_empty_risk_list_are_supported()
    {
        Assert.Empty(AiOutputRules.ValidateTasks(AiOutputRules.Parse<AiTaskGenerationResult>(TestAiProvider.ValidTasks).Tasks));
        Assert.True(AiOutputRules.ValidNarrative(new("Progress", "Completed", "Blockers", "Overdue", [])));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("cycle")]
    [InlineData("self")]
    [InlineData("missing")]
    [InlineData("priority")]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("id")]
    [InlineData("dependencies")]
    [InlineData("null")]
    [InlineData("too-many")]
    public void Invalid_task_content_and_dependency_graphs_are_rejected(string kind)
    {
        var tasks = TestAiProvider.Proposals.ToList();
        switch (kind)
        {
            case "duplicate": tasks[1] = tasks[1] with { Id = "base" }; break;
            case "cycle": tasks[0] = tasks[0] with { DependsOn = ["feature"] }; break;
            case "self": tasks[0] = tasks[0] with { DependsOn = ["base"] }; break;
            case "missing": tasks[0] = tasks[0] with { DependsOn = ["external-id"] }; break;
            case "priority": tasks[0] = tasks[0] with { Priority = "Urgent" }; break;
            case "title": tasks[0] = tasks[0] with { Title = " " }; break;
            case "description": tasks[0] = tasks[0] with { Description = new string('a', 20001) }; break;
            case "id": tasks[0] = tasks[0] with { Id = "../task" }; break;
            case "dependencies": tasks[1] = tasks[1] with { DependsOn = ["base", "base"] }; break;
            case "null": tasks[0] = null!; break;
            case "too-many": tasks = Enumerable.Range(0, 21).Select(i => new AiTaskProposal("task" + i, "Task", null, "Low", [])).ToList(); break;
        }
        Assert.NotEmpty(AiOutputRules.ValidateTasks(tasks));
    }

    [Fact]
    public void Acceptance_requires_explicit_confirmation_and_a_token()
    {
        var errors = AiRequestValidator.Validate(new AiTaskAcceptanceRequestDto(null, false, TestAiProvider.Proposals));
        Assert.Contains("accepted", errors.Keys); Assert.Contains("previewToken", errors.Keys);
        Assert.NotEmpty(AiRequestValidator.Validate(new AiTaskProposalRequestDto(new string('x', 10001))));
        Assert.NotEmpty(AiRequestValidator.Validate(new AiTaskProposalRequestDto(" ")));
    }

    [Fact]
    public void Narrative_has_required_bounded_fields()
    {
        Assert.False(AiOutputRules.ValidNarrative(new(null, "Done", "Blocked", "Overdue", [])));
        Assert.False(AiOutputRules.ValidNarrative(new(new string('x', 4001), "Done", "Blocked", "Overdue", [])));
        Assert.False(AiOutputRules.ValidNarrative(new("Overview", "Done", "Blocked", "Overdue", [null!])));
        Assert.False(AiOutputRules.ValidNarrative(new("Overview", "Done", "Blocked", "Overdue", Enumerable.Repeat("Risk", 11).ToArray())));
    }
}
