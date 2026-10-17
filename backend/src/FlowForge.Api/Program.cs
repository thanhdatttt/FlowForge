using FlowForge.Infrastructure;
using FlowForge.Api;
using FlowForge.Api.Realtime;
using FlowForge.Application.Notifications;
using FlowForge.Infrastructure.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpLogging;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;

// init builder with env config
var builder = WebApplication.CreateBuilder(args);
// Replace hosting request logs (which include query tokens) with safe built-in HTTP logging.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// jwt config
var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 bytes.");
var issuer = builder.Configuration["Jwt:Issuer"] ?? "flowforge";
var audience = builder.Configuration["Jwt:Audience"] ?? "flowforge-api";

// sign services
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AttachmentFileActions>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath |
        HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddOptions<NotificationJobOptions>().BindConfiguration("Notifications")
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<IRealtimePublisher, SignalRPublisher>();
builder.Services.AddSingleton<IEmailContentProtector, EmailContentProtector>();
var protection = builder.Services.AddDataProtection().SetApplicationName("FlowForge");
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath)) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Browser WebSocket/SSE transports cannot set an Authorization header.
            if (context.HttpContext.Request.Path.StartsWithSegments("/hubs/updates") &&
                context.Request.Query.TryGetValue("access_token", out var token)) context.Token = token;
            return Task.CompletedTask;
        }
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("verification", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("refresh", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);

// build and migrate db
var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<FlowForgeDbContext>().Database.MigrateAsync();
}

// add middlewares
app.UseHttpLogging();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Development request binding throws for malformed input; preserve its HTTP status.
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError
});
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (ApiFailure failure)
    {
        await Results.Problem(statusCode: failure.Status, title: failure.Message).ExecuteAsync(context);
    }
    catch (DbUpdateConcurrencyException)
    {
        await Results.Problem(statusCode: 409, title: "The resource changed. Reload and retry.").ExecuteAsync(context);
    }
    catch (DbUpdateException failure) when (failure.InnerException is Npgsql.PostgresException { SqlState: "23505" })
    {
        await Results.Problem(statusCode: 409, title: "The resource already exists.").ExecuteAsync(context);
    }
});
app.UseStatusCodePages(async statusContext =>
{
    await Results.Problem(statusCode: statusContext.HttpContext.Response.StatusCode)
        .ExecuteAsync(statusContext.HttpContext);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

/// map endpoints
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapIdentityEndpoints();
app.MapWorkspaceEndpoints();
app.MapEmailVerificationEndpoints();
app.MapInvitationEndpoints();
app.MapProjectEndpoints();
app.MapWorkItemEndpoints();
app.MapCollaborationEndpoints();
app.MapAttachmentActivityEndpoints();
app.MapNotificationEndpoints();
app.MapHub<UpdatesHub>("/hubs/updates", options => options.CloseOnAuthenticationExpiration = true);

app.Run();

public partial class Program;
