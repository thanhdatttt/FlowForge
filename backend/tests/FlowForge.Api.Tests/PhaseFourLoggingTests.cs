using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFourLoggingTests
{
    [Fact]
    public async Task Request_logs_preserve_method_path_status_and_duration_without_query_tokens()
    {
        using var logs = new TestLogCollector();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:FlowForge", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test");
            builder.UseSetting("Jwt:SigningKey", PhaseTwoFixture.SigningKey);
            builder.UseSetting("Notifications:Enabled", "false");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        using var client = factory.CreateClient();
        const string token = "query-secret-that-must-not-appear-in-logs";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live?access_token=" + token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/notifications?access_token=" + token)).StatusCode);
        var requests = logs.Messages.Where(x => x.Category == "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware").ToList();
        Assert.Contains(requests, x => x.Message.Contains("GET") && x.Message.Contains("/health/live") &&
            x.Message.Contains("200") && x.Message.Contains("Duration"));
        Assert.Contains(requests, x => x.Message.Contains("/api/v1/notifications") && x.Message.Contains("401"));
        Assert.DoesNotContain(logs.Messages, x => x.Message.Contains(token, StringComparison.Ordinal));
    }
}

public sealed class TestLogCollector : ILoggerProvider
{
    public ConcurrentQueue<(string Category, string Message)> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Messages);
    public void Dispose() { }
    private sealed class CaptureLogger(string category, ConcurrentQueue<(string, string)> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue((category, formatter(state, exception)));
    }
}
