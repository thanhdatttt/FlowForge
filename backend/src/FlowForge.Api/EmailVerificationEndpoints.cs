using System.Net.Mail;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Validation.Identity;
using FlowForge.Application.Email;
using FlowForge.Domain.Identity;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class EmailVerificationEndpoints
{
    public static IEndpointRouteBuilder MapEmailVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/email-verification").WithTags("Email verification").RequireAuthorization();
        group.MapPost("/request", async (HttpContext http, FlowForgeDbContext db,
            IEmailSender sender, IConfiguration configuration, CancellationToken ct) =>
        {
            var userId = ApiRules.UserId(http);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            ApiRules.Require(user is { IsActive: true }, 401, "Authentication required.");
            // Verification is persistent account state, not a sign-in challenge.
            if (user!.EmailVerifiedAt != null) return Results.NoContent();
            ApiRules.Require(sender.IsConfigured, 503, "Gmail SMTP is not configured.");
            var now = DateTimeOffset.UtcNow;
            ApiRules.Require(!await db.EmailVerificationTokens.AnyAsync(x => x.UserId == userId &&
                x.CreatedAt > now.AddMinutes(-1), ct), 429, "Wait before requesting another verification email.");
            var minutes = configuration.GetValue<int?>("EmailVerification:LifetimeMinutes") ?? 10;
            ApiRules.Require(minutes is >= 1 and <= 30, 503, "Email verification lifetime must be 1–30 minutes.");
            var code = EmailVerificationCodes.Generate();
            var challenge = new EmailVerificationToken
            {
                UserId = userId, NormalizedEmail = user.NormalizedEmail,
                TokenHash = "", CreatedAt = now, ExpiresAt = now.AddMinutes(minutes)
            };
            challenge.TokenHash = EmailVerificationCodes.Hash(code, challenge.Id, userId, configuration["Jwt:SigningKey"]!);
            // Only the newest code is usable. A delivery failure rolls back both
            // its creation and the invalidation of the previous code.
            await db.EmailVerificationTokens.Where(x => x.UserId == userId && x.UsedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), ct);
            db.EmailVerificationTokens.Add(challenge);
            await db.SaveChangesAsync(ct);
            try { await sender.SendVerificationAsync(user.Email, code, minutes, ct); }
            catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException)
            { throw new ApiFailure(503, "Verification email could not be sent. Please retry later."); }
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("verification").Produces(204).ProducesProblem(503).ProducesProblem(429);

        group.MapPost("/confirm", async (EmailVerificationRequestDto request, HttpContext http,
            FlowForgeDbContext db, IConfiguration configuration, CancellationToken ct) =>
        {
            var userId = ApiRules.UserId(http);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            ApiRules.Require(user is { IsActive: true }, 401, "Authentication required.");
            var now = DateTimeOffset.UtcNow;
            var challenge = await db.EmailVerificationTokens.Where(x => x.UserId == userId && x.UsedAt == null)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            ApiRules.Require(challenge != null && challenge.ExpiresAt > now &&
                challenge.FailedAttempts < EmailVerificationCodes.MaxFailedAttempts &&
                challenge.NormalizedEmail == user!.NormalizedEmail && user.EmailVerifiedAt == null,
                400, "Invalid or expired verification code. Request a new code if needed.");
            var errors = EmailVerificationRequestValidator.Validate(request);
            var matches = errors.Count == 0 &&
                EmailVerificationCodes.Matches(request.Code!, challenge!.Id, userId,
                    configuration["Jwt:SigningKey"]!, challenge.TokenHash);
            if (!matches)
            {
                challenge!.FailedAttempts++;
                if (challenge.FailedAttempts >= EmailVerificationCodes.MaxFailedAttempts) challenge.UsedAt = now;
                await db.SaveChangesAsync(ct);
                // Persist failed attempts before returning the error. Throwing here
                // would roll back the counter and permit unlimited guesses.
                await transaction.CommitAsync(ct);
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                return Results.Problem(statusCode: 400, title: "Invalid or expired verification code. Request a new code if needed.");
            }
            user!.EmailVerifiedAt = now;
            user.UpdatedAt = now;
            await db.EmailVerificationTokens.Where(x => x.UserId == userId && x.UsedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("verification").Produces(204).ProducesProblem(400);
        return app;
    }
}
