namespace FlowForge.Application.Ai;

public sealed record AiSummaryWorkItem(Guid Id, string Key, string Title, string? Description,
    string Type, string Status, bool IsCompleted, string Priority, DateOnly? DueDate, bool IsBlocked);
