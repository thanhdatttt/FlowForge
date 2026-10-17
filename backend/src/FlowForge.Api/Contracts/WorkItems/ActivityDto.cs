namespace FlowForge.Api.Contracts.WorkItems;

public sealed record ActivityDto(Guid Id, Guid? ProjectId, Guid? WorkItemId, Guid? ActorUserId, string ActorKind,
    string Action, string EntityType, Guid EntityId, System.Text.Json.JsonElement? BeforeValues,
    System.Text.Json.JsonElement? AfterValues, string? CorrelationId, DateTimeOffset OccurredAt);
