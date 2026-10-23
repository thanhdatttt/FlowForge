using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlowForge.Application.Ai;
using FlowForge.Infrastructure.Ai;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class OllamaCloudAiProviderTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("test-key", "")]
    [InlineData("", "user-selected-model")]
    [InlineData(" ", "user-selected-model")]
    public async Task Missing_configuration_never_sends_a_request(string key, string model)
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("No HTTP call expected."));
        using var client = new HttpClient(handler);
        var provider = Provider(client, key, model);
        Assert.False(provider.IsConfigured);
        Assert.Equal(AiProviderFailure.Unavailable, (await Assert.ThrowsAsync<AiProviderException>(() =>
            provider.GenerateTasksAsync(TaskContext(), default))).Failure);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cloud_requests_use_user_model_bearer_auth_nonstreaming_chat_and_exact_content(bool summary)
    {
        var completion = summary ? TestAiProvider.ValidSummary : TestAiProvider.ValidTasks;
        using var handler = new StubHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://ollama.com/api/chat", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-only-api-key", request.Headers.Authorization.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = json.RootElement;
            Assert.Equal("user-selected-model", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("stream").GetBoolean());
            Assert.False(root.TryGetProperty("format", out _));
            Assert.False(root.TryGetProperty("tools", out _));
            var messages = root.GetProperty("messages"); Assert.Equal(2, messages.GetArrayLength());
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Contains("untrusted", messages[0].GetProperty("content").GetString());
            Assert.Equal("user", messages[1].GetProperty("role").GetString());
            using var context = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            Assert.Equal("Project", context.RootElement.GetProperty("projectName").GetString());
            if (summary) Assert.True(context.RootElement.TryGetProperty("facts", out _));
            else Assert.Equal("Untrusted feature", context.RootElement.GetProperty("featureDescription").GetString());
            return ChatResponse(completion);
        });
        using var client = new HttpClient(handler);
        var provider = Provider(client);
        Assert.True(provider.IsConfigured);
        var result = summary
            ? await provider.SummarizeProjectAsync(new(Guid.NewGuid(), Guid.NewGuid(), "Project", DateTimeOffset.UtcNow,
                DateOnly.FromDateTime(DateTime.UtcNow), new(1, 0, 0, 0, 0), [], true), default)
            : await provider.GenerateTasksAsync(TaskContext(), default);
        Assert.Equal(completion, result); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(401, AiProviderFailure.Unavailable)]
    [InlineData(403, AiProviderFailure.Unavailable)]
    [InlineData(404, AiProviderFailure.Unavailable)]
    [InlineData(500, AiProviderFailure.Unavailable)]
    [InlineData(503, AiProviderFailure.Unavailable)]
    [InlineData(408, AiProviderFailure.Timeout)]
    [InlineData(504, AiProviderFailure.Timeout)]
    [InlineData(400, AiProviderFailure.InvalidResponse)]
    [InlineData(422, AiProviderFailure.InvalidResponse)]
    [InlineData(302, AiProviderFailure.InvalidResponse)]
    public async Task Provider_http_failures_are_classified_without_exposing_response_bodies(int status, AiProviderFailure failure)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent("private-key-and-provider-details") }));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(client).GenerateTasksAsync(TaskContext(), default));
        Assert.Equal(failure, error.Failure); Assert.DoesNotContain("private", error.ToString()); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(0, 1)]
    [InlineData(9999, 3600)]
    public async Task Rate_limits_preserve_bounded_retry_after_without_retries(int seconds, int expected)
    {
        using var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(client).GenerateTasksAsync(TaskContext(), default));
        Assert.Equal(AiProviderFailure.RateLimited, error.Failure); Assert.Equal(expected, error.RetryAfterSeconds);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Rate_limit_without_header_defaults_to_sixty_seconds()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(client).GenerateTasksAsync(TaskContext(), default));
        Assert.Equal(60, error.RetryAfterSeconds);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"done\":false,\"message\":{\"role\":\"assistant\",\"content\":\"text\"}}")]
    [InlineData("{\"done\":true,\"message\":{\"role\":\"tool\",\"content\":\"text\"}}")]
    [InlineData("{\"done\":true,\"message\":{\"role\":\"assistant\",\"content\":null}}")]
    [InlineData("{\"done\":true,\"message\":{\"role\":\"assistant\",\"content\":\" \"}}")]
    public async Task Malformed_or_incomplete_envelopes_are_rejected(string json)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AiProviderException>(() => Provider(client).GenerateTasksAsync(TaskContext(), default));
        Assert.Equal(AiProviderFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Oversized_completions_and_response_bodies_are_rejected()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(ChatResponse(new string('a', AiOutputRules.MaxOutputCharacters + 1))));
        using var client = new HttpClient(handler);
        Assert.Equal(AiProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<AiProviderException>(() =>
            Provider(client).GenerateTasksAsync(TaskContext(), default))).Failure);
        using var largeHandler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(new string('a', OllamaCloudAiProvider.MaxResponseBytes + 1)) }));
        using var largeClient = new HttpClient(largeHandler);
        Assert.Equal(AiProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<AiProviderException>(() =>
            Provider(largeClient).GenerateTasksAsync(TaskContext(), default))).Failure);
    }

    [Fact]
    public async Task Body_limit_also_applies_when_content_length_is_unknown()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new UnknownLengthContent(OllamaCloudAiProvider.MaxResponseBytes + 1) }));
        using var client = new HttpClient(handler);
        Assert.Equal(AiProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<AiProviderException>(() =>
            Provider(client).GenerateTasksAsync(TaskContext(), default))).Failure);
    }

    [Fact]
    public async Task Transport_failure_is_safe_and_cancellation_reaches_http_handler()
    {
        using var failed = new StubHandler((_, _) => throw new HttpRequestException("private-transport-details"));
        using var failedClient = new HttpClient(failed);
        Assert.Equal(AiProviderFailure.Unavailable, (await Assert.ThrowsAsync<AiProviderException>(() =>
            Provider(failedClient).GenerateTasksAsync(TaskContext(), default))).Failure);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var waiting = new StubHandler(async (_, ct) =>
        {
            entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return ChatResponse("never");
        });
        using var waitingClient = new HttpClient(waiting); using var cancellation = new CancellationTokenSource();
        var pending = Provider(waitingClient).GenerateTasksAsync(TaskContext(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static OllamaCloudAiProvider Provider(HttpClient client, string key = "test-only-api-key", string model = "user-selected-model") =>
        new(client, Options.Create(new OllamaCloudOptions { ApiKey = key, Model = model }));
    private static AiTaskGenerationContext TaskContext() => new(Guid.NewGuid(), Guid.NewGuid(), "Project", "Untrusted feature");
    private static HttpResponseMessage ChatResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { done = true, message = new { role = "assistant", content } }), Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return send(request, cancellationToken); }
    }

    private sealed class UnknownLengthContent(int size) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await stream.WriteAsync(new byte[size]);
    }
}
