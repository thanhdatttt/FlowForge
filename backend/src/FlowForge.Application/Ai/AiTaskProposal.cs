namespace FlowForge.Application.Ai;

public sealed record AiTaskProposal(string? Id, string? Title, string? Description, string? Priority, IReadOnlyList<string>? DependsOn);
