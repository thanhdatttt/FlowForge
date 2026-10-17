using System.Net;
using System.Net.Mail;
using FlowForge.Application.Email;
using Microsoft.Extensions.Configuration;

namespace FlowForge.Infrastructure.Email;

public sealed class GmailSmtpEmailSender(IConfiguration configuration) : IEmailSender
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
        if (!IsConfigured) throw new InvalidOperationException("Gmail SMTP is not configured.");
        using var client = new SmtpClient("smtp.gmail.com", 587)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(configuration["Smtp:Username"], configuration["Smtp:AppPassword"])
        };
        using var message = new MailMessage
        {
            From = new MailAddress(configuration["Smtp:Username"]!, "FlowForge"),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(recipient);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { await client.SendMailAsync(message, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new SmtpException("Gmail SMTP delivery timed out."); }
    }
}
