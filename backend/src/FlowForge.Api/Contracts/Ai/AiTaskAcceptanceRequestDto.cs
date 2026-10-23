using FlowForge.Application.Ai;

namespace FlowForge.Api.Contracts.Ai;

public sealed record AiTaskAcceptanceRequestDto(string? PreviewToken, bool Accepted, IReadOnlyList<AiTaskProposal>? Tasks);
