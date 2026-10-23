using FlowForge.Application.Ai;
using FlowForge.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace FlowForge.Api;

public sealed class AiProviderCalls(IAiProvider provider, IOptions<AiOptions> options, ILogger<AiProviderCalls> logger)
{
    public Task<AiTaskGenerationResult> TasksAsync(AiTaskGenerationContext context, HttpContext http, CancellationToken ct) =>
        RunAsync<AiTaskGenerationResult>(token => provider.GenerateTasksAsync(context, token), http, ct,
            value => AiOutputRules.ValidateTasks(value.Tasks).Count == 0);

    public Task<AiProjectNarrative> SummaryAsync(AiProjectSummaryContext context, HttpContext http, CancellationToken ct) =>
        RunAsync<AiProjectNarrative>(token => provider.SummarizeProjectAsync(context, token), http, ct, AiOutputRules.ValidNarrative);

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<string>> call, HttpContext http, CancellationToken ct, Func<T, bool> valid) where T : class
    {
        ApiRules.Require(provider.IsConfigured, 503, "AI provider is not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var response = await call(timeout.Token).WaitAsync(timeout.Token);
            var result = AiOutputRules.Parse<T>(response);
            if (!valid(result)) throw new AiProviderException(AiProviderFailure.InvalidResponse);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ApiFailure(504, "AI provider request timed out."); }
        catch (OperationCanceledException) { throw; }
        catch (AiProviderException error)
        {
            if (error.Failure == AiProviderFailure.RateLimited)
                http.Response.Headers.RetryAfter = Math.Clamp(error.RetryAfterSeconds ?? 60, 1, 3600).ToString(System.Globalization.CultureInfo.InvariantCulture);
            throw error.Failure switch
            {
                AiProviderFailure.Unavailable => new ApiFailure(503, "AI provider is unavailable or not configured."),
                AiProviderFailure.RateLimited => new ApiFailure(429, "AI provider rate limit exceeded."),
                AiProviderFailure.Timeout => new ApiFailure(504, "AI provider request timed out."),
                _ => new ApiFailure(502, "AI provider returned an invalid response.")
            };
        }
        catch (Exception error)
        {
            // Provider messages, prompts and completions may contain secrets or private project content.
            logger.LogWarning("AI provider request failed ({ErrorType}).", error.GetType().Name);
            throw new ApiFailure(502, "AI provider request failed.");
        }
    }
}
