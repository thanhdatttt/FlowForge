using FlowForge.Application.Ai;
using FlowForge.Api;
using FlowForge.Infrastructure.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using System.Net;
using System.Text.Json;

namespace FlowForge.Api.Tests;

public sealed class PhaseSixAiHostTests
{
    [Theory]
    [InlineData("TimeoutSeconds")]
    [InlineData("PreviewLifetimeMinutes")]
    [InlineData("MaxContextItems")]
    public void Invalid_ai_limits_prevent_startup(string setting)
    {
        using var factory = Factory().WithWebHostBuilder(b => b.UseSetting("Ai:" + setting, "0"));
        Assert.Contains(setting, Assert.Throws<OptionsValidationException>(() => factory.CreateClient()).Message);
    }

    [Fact]
    public async Task Default_host_has_no_configured_model_or_provider()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAiProvider>();
        Assert.IsType<OllamaCloudAiProvider>(provider); Assert.False(provider.IsConfigured);
        var error = await Assert.ThrowsAsync<AiProviderException>(() => provider.GenerateTasksAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Project", "Feature"), default));
        Assert.Equal(AiProviderFailure.Unavailable, error.Failure);
        Assert.True((await client.GetAsync("/health/live")).IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("test-key", "", false)]
    [InlineData("", "user-selected-model", false)]
    [InlineData("test-key", "user-selected-model", true)]
    public void Ollama_configuration_is_bound_without_selecting_a_default_model(string key, string model, bool expected)
    {
        using var factory = Factory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ollama:ApiKey", key); builder.UseSetting("Ollama:Model", model);
        });
        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
        Assert.Equal(expected, scope.ServiceProvider.GetRequiredService<IAiProvider>().IsConfigured);
    }

    [Theory]
    [InlineData("ApiKey", "bad\r\nkey")]
    [InlineData("Model", "bad model name")]
    public void Invalid_ollama_header_or_model_configuration_is_rejected(string setting, string value)
    {
        using var factory = Factory().WithWebHostBuilder(builder => builder.UseSetting("Ollama:" + setting, value));
        Assert.Contains(setting, Assert.Throws<OptionsValidationException>(() => factory.CreateClient()).Message);
    }

    [Fact]
    public async Task Registered_ollama_adapter_and_api_validation_work_with_mocked_cloud_transport()
    {
        using var factory = Factory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ollama:ApiKey", "test-only-key"); builder.UseSetting("Ollama:Model", "user-selected-model");
            builder.ConfigureServices(services => services.AddHttpClient<OllamaCloudAiProvider>()
                .ConfigurePrimaryHttpMessageHandler(() => new MockCloudHandler()));
        });
        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
        var calls = scope.ServiceProvider.GetRequiredService<AiProviderCalls>();
        var http = new DefaultHttpContext();
        var tasks = await calls.TasksAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Project", "Feature"), http, default);
        Assert.Equal(2, tasks.Tasks!.Count);
        var summary = await calls.SummaryAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Project", DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow), new(1, 0, 0, 0, 0), [], true), http, default);
        Assert.Equal("Overview", summary.Overview);
    }

    private sealed class MockCloudHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://ollama.com/api/chat", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-only-key", request.Headers.Authorization!.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("user-selected-model", body.RootElement.GetProperty("model").GetString());
            var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            var content = prompt.StartsWith("Summarize", StringComparison.Ordinal) ? TestAiProvider.ValidSummary : TestAiProvider.ValidTasks;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { done = true, message = new { role = "assistant", content } })) };
        }
    }

    private static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Jwt:SigningKey", PhaseTwoFixture.SigningKey);
        builder.UseSetting("ConnectionStrings:FlowForge", "Host=127.0.0.1;Port=1;Database=unused;Username=test;Password=test");
        builder.UseSetting("Database:AutoMigrate", "false");
        builder.UseSetting("Ollama:ApiKey", ""); builder.UseSetting("Ollama:Model", "");
        builder.UseSetting("Notifications:Enabled", "false"); builder.UseSetting("Workflows:Enabled", "false");
    });
}
