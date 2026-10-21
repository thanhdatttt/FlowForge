using System.Net;
using System.Net.Mail;
using FlowForge.Application.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowForge.Infrastructure.Email;

public sealed class GmailSmtpEmailSender(IConfiguration configuration, ILogger<GmailSmtpEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Smtp:Username"]) &&
        !string.IsNullOrWhiteSpace(configuration["Smtp:AppPassword"]);

    // send email verification
    public Task SendVerificationAsync(string recipient, string code, int lifetimeMinutes, CancellationToken cancellationToken) =>
        SendAsync(recipient, "Verify your FlowForge email",
            $"Your FlowForge email verification code is:\n\n{code}\n\n" +
            $"Enter this 6-digit code to verify your email. It expires in {lifetimeMinutes} minutes. " +
            "If you did not request this email, ignore it.", cancellationToken);

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Gmail SMTP is not configured. Set Smtp:Username and Smtp:AppPassword in the API environment.");
            throw new InvalidOperationException("Gmail SMTP is not configured.");
        }
        using var client = CreateClient();
        using var message = new MailMessage
        {
            From = new MailAddress(configuration["Smtp:Username"]!.Trim(), "FlowForge"),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(recipient);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { await client.SendMailAsync(message, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Gmail SMTP delivery timed out after 20 seconds.");
            throw new SmtpException("Gmail SMTP delivery timed out.");
        }
        catch (SmtpException exception)
        {
            // Provider messages can contain account/recipient addresses. Log only safe diagnostics.
            logger.LogWarning("Gmail SMTP delivery failed. SMTP status: {SmtpStatus}; failure type: {FailureType}. " +
                "For status 535, check the Gmail account and generate a new App Password.",
                (int)exception.StatusCode, exception.InnerException?.GetType().Name ?? exception.GetType().Name);
            throw;
        }
    }

    internal SmtpClient CreateClient() => new("smtp.gmail.com", 587)
    {
        EnableSsl = true,
        UseDefaultCredentials = false,
        // Gmail displays App Passwords in groups. Formatting spaces are not part of the credential.
        Credentials = new NetworkCredential(configuration["Smtp:Username"]?.Trim(),
            string.Concat((configuration["Smtp:AppPassword"] ?? "").Where(character => !char.IsWhiteSpace(character))))
    };
}
