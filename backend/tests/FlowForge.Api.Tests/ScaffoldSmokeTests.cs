using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class ScaffoldSmokeTests
{
    [Fact]
    public async Task Host_exposes_liveness_and_openapi_in_development()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:FlowForge",
                    "Host=localhost;Database=flowforge;Username=flowforge;Password=test");
                builder.UseSetting("Jwt:SigningKey", "scaffold-test-signing-key-at-least-32-bytes");
            });
        using var client = factory.CreateClient();

        var liveness = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);

        var openApi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
        var paths = document.RootElement.GetProperty("paths");
        var registration = paths.GetProperty("/api/v1/auth/register").GetProperty("post");
        var requestSchema = registration.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        var responseSchema = registration.GetProperty("responses").GetProperty("201").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        Assert.EndsWith("/RegisterRequestDto", requestSchema);
        Assert.EndsWith("/AuthTokenResponseDto", responseSchema);
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out _));

        var unauthorized = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var unauthorizedWorkspaces = await client.GetAsync("/api/v1/workspaces");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedWorkspaces.StatusCode);
    }
}
