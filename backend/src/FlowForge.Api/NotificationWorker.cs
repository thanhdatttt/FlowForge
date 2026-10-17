using FlowForge.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace FlowForge.Api;

public sealed class NotificationWorker(IServiceScopeFactory scopes, IOptions<NotificationJobOptions> options,
    TimeProvider clock, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return Task.CompletedTask;
        var poll = TimeSpan.FromSeconds(options.Value.PollSeconds);
        // Each loop owns its scopes, timeout, and error handling. Slow or failed jobs cannot
        // hold up the other queues, and each job type still has only one owner per host.
        return Task.WhenAll(
            RunAsync("overdue", TimeSpan.FromMinutes(options.Value.OverdueIntervalMinutes), 1, async (services, ct) =>
            {
                await services.GetRequiredService<OverdueChecker>().CheckAsync(ct);
                return false;
            }, stoppingToken),
            RunAsync("outbox", poll, options.Value.BatchSize,
                (services, ct) => services.GetRequiredService<NotificationDispatcher>().DispatchNextAsync(ct), stoppingToken),
            RunAsync("email", poll, options.Value.BatchSize,
                (services, ct) => services.GetRequiredService<EmailDeliveryProcessor>().SendNextAsync(ct), stoppingToken));
    }

    private async Task RunAsync(string job, TimeSpan interval, int batchSize,
        Func<IServiceProvider, CancellationToken, Task<bool>> run, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                for (var i = 0; i < batchSize; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));
                    if (!await run(scope.ServiceProvider, timeout.Token)) break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // Exception messages can contain SQL, addresses, or secrets.
                logger.LogError("Notification job {Job} cycle failed ({ErrorType}); retrying after its interval.", job, error.GetType().Name);
            }
            try { await Task.Delay(interval, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
