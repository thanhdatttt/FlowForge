using FlowForge.Application.Ai;

namespace FlowForge.Api.Contracts.Ai;

public sealed record AiProjectSummaryDto(Guid ProjectId, DateTimeOffset AsOf, DateOnly TodayUtc,
    AiProjectFacts Facts, string Status, AiProjectNarrative? Narrative, bool IsAiGenerated,
    int ContextItemCount, bool ContextTruncated);
