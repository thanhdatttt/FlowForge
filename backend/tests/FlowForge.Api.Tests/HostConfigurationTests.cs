using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class HostConfigurationTests
{
    [Fact]
    public async Task Browser_session_without_cookie_is_unauthorized_without_using_database()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:FlowForge", "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1");
            builder.UseSetting("Jwt:SigningKey", PhaseTwoFixture.SigningKey);
            builder.UseSetting("Database:AutoMigrate", "false");
        });
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/api/v1/auth/session", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Production_hides_openapi_and_unavailable_database_fails_readiness_but_not_liveness()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:FlowForge", "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1");
            builder.UseSetting("Jwt:SigningKey", PhaseTwoFixture.SigningKey);
            builder.UseSetting("Database:AutoMigrate", "false");
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Missing_or_short_signing_keys_prevent_startup(string key)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningKey", key);
            builder.UseSetting("ConnectionStrings:FlowForge", "Host=localhost;Database=test;Username=test;Password=test");
        });
        var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("at least 32 bytes", failure.Message);
    }
}
