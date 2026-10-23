using FlowForge.Application.Ai;

namespace FlowForge.Api.Contracts.Ai;

public sealed record AiTaskPreviewDto(string PreviewToken, DateTimeOffset ExpiresAt,
    IReadOnlyList<AiTaskProposal> Tasks, bool IsAiGenerated = true);
