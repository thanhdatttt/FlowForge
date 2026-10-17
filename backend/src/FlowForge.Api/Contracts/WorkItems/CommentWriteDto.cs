namespace FlowForge.Api.Contracts.WorkItems;

public sealed record CommentWriteDto(string? Body, long? Version);
