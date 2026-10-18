using FlowForge.Infrastructure.Workflows;
using Microsoft.Extensions.Options;

namespace FlowForge.Api;

public sealed class WorkflowWorker(IServiceScopeFactory scopes, IOptions<WorkflowJobOptions> options,
    TimeProvider clock, ILogger<WorkflowWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return Task.CompletedTask;
        var poll = TimeSpan.FromSeconds(options.Value.PollSeconds);
        return Task.WhenAll(
            RunAsync("schedule", poll, options.Value.BatchSize,
                (services, ct) => services.GetRequiredService<WorkflowScheduler>().ScheduleNextAsync(ct), stoppingToken),
            RunAsync("execute", poll, options.Value.BatchSize,
                (services, ct) => services.GetRequiredService<WorkflowRunner>().RunNextAsync(ct), stoppingToken),
            RunAsync("dates", TimeSpan.FromMinutes(options.Value.DateScanIntervalMinutes), 1, async (services, ct) =>
            { await services.GetRequiredService<WorkflowDateScanner>().ScanAsync(ct); return false; }, stoppingToken));
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
            { logger.LogError("Workflow job {Job} cycle failed ({ErrorType}); retrying after its interval.", job, error.GetType().Name); }
            try { await Task.Delay(interval, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
