namespace FlowForge.Application.Ai;

public sealed record AiProjectSummaryContext(Guid WorkspaceId, Guid ProjectId, string ProjectName,
    DateTimeOffset AsOf, DateOnly TodayUtc, AiProjectFacts Facts, IReadOnlyList<AiSummaryWorkItem> Items, bool ContextTruncated);
