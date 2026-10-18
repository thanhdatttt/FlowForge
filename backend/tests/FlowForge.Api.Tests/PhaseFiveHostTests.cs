using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFiveHostTests
{
    [Theory]
    [InlineData("PollSeconds")]
    [InlineData("MaxAttempts")]
    [InlineData("MaxDepth")]
    [InlineData("BatchSize")]
    [InlineData("DateScanIntervalMinutes")]
    public void Invalid_workflow_job_configuration_prevents_startup(string setting)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningKey", PhaseTwoFixture.SigningKey);
            builder.UseSetting("ConnectionStrings:FlowForge", "Host=127.0.0.1;Port=1;Database=unused;Username=test;Password=test");
            builder.UseSetting("Database:AutoMigrate", "false");
            builder.UseSetting("Notifications:Enabled", "false");
            builder.UseSetting("Workflows:" + setting, "0");
        });
        var failure = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains(setting, failure.Message);
    }
}
