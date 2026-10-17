using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlowForge.Infrastructure.Notifications;

public sealed class NotificationJobOptions
{
    public bool Enabled { get; set; } = true;
    [Range(1, 300)] public int PollSeconds { get; set; } = 5;
    [Range(1, 1440)] public int OverdueIntervalMinutes { get; set; } = 5;
    [Range(1, 500)] public int BatchSize { get; set; } = 50;
    [Range(1, 10)] public int MaxAttempts { get; set; } = 5;
}

public static class NotificationRetry
{
    public static TimeSpan Delay(int attempt) => TimeSpan.FromSeconds(Math.Min(900, 5 * Math.Pow(2, Math.Clamp(attempt - 1, 0, 8))));
    public static bool IsTransient(Exception error) => error switch
    {
        SmtpException smtp => (int)smtp.StatusCode < 500,
        NpgsqlException postgres => postgres.IsTransient,
        DbUpdateException { InnerException: NpgsqlException postgres } => postgres.IsTransient,
        TimeoutException or IOException or OperationCanceledException => true,
        _ => false
    };
    // Never persist provider exception messages: they can contain email addresses, SQL, or secrets.
    public static string SafeError(Exception error) => IsTransient(error) ? "Transient delivery failure." : "Permanent delivery failure.";
}
