namespace FlowForge.Application.Ai;

public sealed record AiTaskGenerationResult(IReadOnlyList<AiTaskProposal>? Tasks);
