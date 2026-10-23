using System.ComponentModel.DataAnnotations;

namespace FlowForge.Infrastructure.Ai;

public sealed class AiOptions
{
    [Range(1, 120)] public int TimeoutSeconds { get; set; } = 30;
    [Range(1, 60)] public int PreviewLifetimeMinutes { get; set; } = 15;
    [Range(1, 100)] public int MaxContextItems { get; set; } = 50;
}
