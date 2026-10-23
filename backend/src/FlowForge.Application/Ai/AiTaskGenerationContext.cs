namespace FlowForge.Application.Ai;

public sealed record AiTaskGenerationContext(Guid WorkspaceId, Guid ProjectId, string ProjectName, string FeatureDescription);
