using System.Text.Json;
using FlowForge.Application.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowForge.Api.Tests;

public sealed class PhaseSixAiFixture : PhaseTwoFixture
{
    public TestAiProvider Provider { get; } = new();
    public TestAiClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Ai:TimeoutSeconds", "1");
        builder.UseSetting("Ai:MaxContextItems", "2");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiProvider>(); services.AddSingleton<IAiProvider>(Provider);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

public sealed class TestAiClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

// Only the test host registers this fake; no model, SDK, credentials or external network is involved.
public sealed class TestAiProvider : IAiProvider
{
    public bool IsConfigured { get; set; } = true;
    public int Calls { get; private set; }
    public AiTaskGenerationContext? TaskContext { get; private set; }
    public AiProjectSummaryContext? SummaryContext { get; private set; }
    public Func<CancellationToken, Task<string>>? Tasks { get; set; }
    public Func<CancellationToken, Task<string>>? Summary { get; set; }
    public static readonly AiTaskProposal[] Proposals =
    [new("base", "Foundation", "Build the foundation", "High", []), new("feature", "Feature", "Build the feature", "Medium", ["base"])];
    public static string ValidTasks => JsonSerializer.Serialize(new AiTaskGenerationResult(Proposals), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public static string ValidSummary => JsonSerializer.Serialize(new AiProjectNarrative("Overview", "Completed", "Blockers", "Overdue", ["Possible risk"]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public void Reset()
    {
        IsConfigured = true; Calls = 0; TaskContext = null; SummaryContext = null; Tasks = null; Summary = null;
    }
    public Task<string> GenerateTasksAsync(AiTaskGenerationContext context, CancellationToken cancellationToken)
    {
        Calls++; TaskContext = context;
        return Tasks?.Invoke(cancellationToken) ?? Task.FromResult(ValidTasks);
    }
    public Task<string> SummarizeProjectAsync(AiProjectSummaryContext context, CancellationToken cancellationToken)
    {
        Calls++; SummaryContext = context;
        return Summary?.Invoke(cancellationToken) ?? Task.FromResult(ValidSummary);
    }
}
