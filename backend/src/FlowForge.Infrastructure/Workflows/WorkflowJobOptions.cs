using System.ComponentModel.DataAnnotations;

namespace FlowForge.Infrastructure.Workflows;

public sealed class WorkflowJobOptions
{
    public bool Enabled { get; set; } = true;
    [Range(1, 300)] public int PollSeconds { get; set; } = 5;
    [Range(1, 1440)] public int DateScanIntervalMinutes { get; set; } = 5;
    [Range(1, 500)] public int BatchSize { get; set; } = 50;
    [Range(1, 10)] public int MaxAttempts { get; set; } = 5;
    [Range(1, 20)] public int MaxDepth { get; set; } = 8;
}
