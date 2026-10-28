using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class HostConfigurationTests
{
    [Theory]
    [InlineData("http://localhost:5173", "http://localhost:5173", true)]
    [InlineData("http://localhost:5173", "https://untrusted.example", false)]
    [InlineData("", "http://localhost:5173", false)]
    public async Task Cors_preflight_allows_only_the_configured_frontend(string configuredOrigin, string requestOrigin, bool allowed)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:FlowForge", "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1");
            builder.UseSetting("Jwt:SigningKey", PhaseTwoFixture.SigningKey);
            builder.UseSetting("Database:AutoMigrate", "false");
            builder.UseSetting("Notifications:Enabled", "false");
            builder.UseSetting("Workflows:Enabled", "false");
            builder.UseSetting("Frontend:Origin", configuredOrigin);
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/session");
        request.Headers.Add("Origin", requestOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-flowforge-session");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        if (allowed)
        {
            Assert.Equal(requestOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
        else
        {
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        }
    }

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
