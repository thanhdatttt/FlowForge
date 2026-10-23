namespace FlowForge.Application.Ai;

// Narrative is an AI interpretation, not an authoritative source for numeric metrics.
public sealed record AiProjectNarrative(string? Overview, string? CompletedWork, string? Blockers,
    string? OverdueWork, IReadOnlyList<string>? Risks);
