namespace FlowForge.Application.Ai;

// Implement this boundary in your own adapter. No vendor SDK, model or network endpoint is selected here.
// Return the documented JSON envelope, treat project text as untrusted data, and honor cancellation.
public interface IAiProvider
{
    bool IsConfigured { get; }
    Task<string> GenerateTasksAsync(AiTaskGenerationContext context, CancellationToken cancellationToken);
    Task<string> SummarizeProjectAsync(AiProjectSummaryContext context, CancellationToken cancellationToken);
}
