using System.Data.Common;
using FlowForge.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FlowForge.Api.Tests;

public sealed class PhaseFiveFixture : PhaseTwoFixture
{
    public WorkflowFailureInterceptor Failure { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddDbContext<FlowForgeDbContext>(o => o.AddInterceptors(Failure)));
    }
    public async Task InScope(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }
}

public sealed class WorkflowFailureInterceptor : DbCommandInterceptor
{
    public Guid? WorkItemId { get; set; }
    public bool Permanent { get; set; }
    public bool BlockUntilCancelled { get; set; }
    public bool FailEveryTime { get; set; }
    public int FailureCount { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (WorkItemId != null && command.CommandText.Contains("INSERT INTO comments", StringComparison.Ordinal) &&
            command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == WorkItemId))
        {
            FailureCount++;
            if (BlockUntilCancelled) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (!FailEveryTime) WorkItemId = null;
            if (Permanent) throw new InvalidOperationException("Test provider secret must never appear in history");
            throw new IOException("Test provider secret must never appear in history");
        }
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
