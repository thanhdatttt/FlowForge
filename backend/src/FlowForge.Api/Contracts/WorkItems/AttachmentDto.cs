namespace FlowForge.Api.Contracts.WorkItems;

public sealed record AttachmentDto(Guid Id, string OriginalName, string ContentType, long SizeBytes,
    Guid UploadedByUserId, DateTimeOffset CreatedAt);
