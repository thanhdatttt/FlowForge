using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowForge.Api.Tests;

public sealed class PhaseSixFixture : PhaseTwoFixture
{
    // UTC and Vietnam have different calendar dates at this instant.
    public static readonly DateTimeOffset Now = new(2030, 4, 4, 23, 30, 0, TimeSpan.Zero);
    public static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new DashboardClock());
        });
    }

    private sealed class DashboardClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
