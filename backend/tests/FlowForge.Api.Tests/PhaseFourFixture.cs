using System.Collections.Concurrent;
using System.Data.Common;
using FlowForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using FlowForge.Api.Realtime;
using FlowForge.Application.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowForge.Api.Tests;

public sealed class PhaseFourFixture : PhaseTwoFixture
{
    public TestNotificationClock Clock { get; } = new();
    public RecordingRealtimePublisher Realtime { get; } = new();
    public OverdueFailureInterceptor OverdueFailure { get; } = new();
    public TestLogCollector Logs { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureServices(services =>
        {
            services.AddDbContext<FlowForgeDbContext>(options => options.AddInterceptors(OverdueFailure));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IRealtimePublisher>();
            services.AddSingleton<IRealtimePublisher>(provider =>
            {
                Realtime.Inner = new SignalRPublisher(provider.GetRequiredService<IHubContext<UpdatesHub>>());
                return Realtime;
            });
        });
    }
    public async Task InScope(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }
}

public sealed class TestNotificationClock : TimeProvider
{
    private TimeSpan offset;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    public void Advance(TimeSpan amount) => offset += amount;
}

public sealed class RecordingRealtimePublisher : IRealtimePublisher
{
    public IRealtimePublisher? Inner { get; set; }
    public int FailuresRemaining { get; set; }
    public ConcurrentQueue<(RealtimeUpdate Update, IReadOnlyList<Guid> Recipients)> Updates { get; } = new();
    public ConcurrentQueue<(Guid Recipient, Guid Notification)> Notifications { get; } = new();
    public async Task PublishProjectAsync(RealtimeUpdate update, IReadOnlyList<Guid> recipients, CancellationToken ct)
    {
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new IOException("Test-only failure containing a secret that must not be persisted");
        }
        if (Inner != null) await Inner.PublishProjectAsync(update, recipients, ct);
        Updates.Enqueue((update, recipients));
    }
    public async Task PublishNotificationAsync(Guid recipientId, Guid notificationId, CancellationToken ct)
    {
        if (Inner != null) await Inner.PublishNotificationAsync(recipientId, notificationId, ct);
        Notifications.Enqueue((recipientId, notificationId));
    }
}

public sealed class OverdueFailureInterceptor : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public int FailureCount;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && command.CommandText.Contains("-- Overdue candidates", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref FailureCount);
            throw new InvalidOperationException("Test-only overdue scanner failure");
        }
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
