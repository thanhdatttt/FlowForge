namespace FlowForge.Application.Ai;

public sealed class AiProviderException(AiProviderFailure failure, int? retryAfterSeconds = null) : Exception("AI provider request failed.")
{
    public AiProviderFailure Failure { get; } = failure;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
