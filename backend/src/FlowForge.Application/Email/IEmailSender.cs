namespace FlowForge.Application.Email;

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendVerificationAsync(string recipient, string code, int lifetimeMinutes, CancellationToken cancellationToken);
}
