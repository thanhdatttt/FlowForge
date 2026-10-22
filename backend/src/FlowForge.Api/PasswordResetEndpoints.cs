using System.Net.Mail;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Validations;
using FlowForge.Api.Validations.Identity;
using FlowForge.Application.Email;
using FlowForge.Domain.Identity;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class PasswordResetEndpoints
{
    private const string InvalidCode = "Invalid or expired verification code. Request a new code if needed.";
    private const string InvalidToken = "Invalid or expired password reset token. Request a new code.";

    public static IEndpointRouteBuilder MapPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Password reset").RequireRateLimiting("password-reset");
        group.MapPost("/forgot-password", async (ForgotPasswordRequestDto request, FlowForgeDbContext db,
            IEmailSender sender, IConfiguration configuration, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            InputRules.Email(request.Email, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            ApiRules.Require(sender.IsConfigured, 503, "Gmail SMTP is not configured.");
            var email = AuthService.NormalizeEmail(request.Email!);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE normalized_email = {email} FOR NO KEY UPDATE")
                .SingleOrDefaultAsync(ct);
            // Keep the public response identical for missing, inactive, and rate-limited accounts.
            if (user is not { IsActive: true }) return Results.NoContent();
            var now = DateTimeOffset.UtcNow;
            if (await db.PasswordResetChallenges.AnyAsync(x => x.UserId == user.Id && x.CreatedAt > now.AddMinutes(-1), ct))
                return Results.NoContent();
            var code = EmailVerificationCodes.Generate();
            var challenge = new PasswordResetChallenge
            {
                UserId = user.Id, NormalizedEmail = email, CodeHash = "", CreatedAt = now, ExpiresAt = now.AddMinutes(10)
            };
            challenge.CodeHash = HashCode(code, challenge, configuration);
            await db.PasswordResetChallenges.Where(x => x.UserId == user.Id && x.UsedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), ct);
            db.PasswordResetChallenges.Add(challenge);
            await db.SaveChangesAsync(ct);
            try
            {
                await sender.SendAsync(user.Email, "Reset your FlowForge password",
                    $"Your FlowForge password reset code is:\n\n{code}\n\n" +
                    "This code expires in 10 minutes. If you did not request a password reset, ignore this email.", ct);
            }
            catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException)
            { throw new ApiFailure(503, "Password reset email could not be sent. Please retry later."); }
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).Produces(204).ProducesValidationProblem().ProducesProblem(503).ProducesProblem(429);

        group.MapPost("/forgot-password/verify", async (VerifyPasswordResetRequestDto request,
            FlowForgeDbContext db, IConfiguration configuration, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            InputRules.Email(request.Email, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var email = AuthService.NormalizeEmail(request.Email!);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE normalized_email = {email} FOR NO KEY UPDATE")
                .SingleOrDefaultAsync(ct);
            ApiRules.Require(user is { IsActive: true }, 400, InvalidCode);
            var now = DateTimeOffset.UtcNow;
            var challenge = await db.PasswordResetChallenges.Where(x => x.UserId == user!.Id && x.UsedAt == null)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            ApiRules.Require(challenge != null && challenge.ExpiresAt > now && challenge.ConfirmedAt == null &&
                challenge.FailedAttempts < EmailVerificationCodes.MaxFailedAttempts && challenge.NormalizedEmail == email, 400, InvalidCode);
            var codeErrors = EmailVerificationRequestValidator.Validate(new(request.Code));
            if (codeErrors.Count > 0 || !MatchesCode(request.Code!, challenge!, configuration))
            {
                challenge!.FailedAttempts++;
                if (challenge.FailedAttempts >= EmailVerificationCodes.MaxFailedAttempts) challenge.UsedAt = now;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Problem(statusCode: 400, title: InvalidCode);
            }
            var token = SecureTokens.Generate();
            challenge!.ConfirmedAt = now;
            challenge.ResetTokenHash = SecureTokens.Hash(token);
            challenge.ResetTokenExpiresAt = now.AddMinutes(10);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(new PasswordResetTokenResponseDto(token, challenge.ResetTokenExpiresAt.Value));
        }).Produces<PasswordResetTokenResponseDto>().ProducesValidationProblem().ProducesProblem(429);

        group.MapPost("/reset-password", async (ResetPasswordRequestDto request, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            InputRules.Token(request.ResetToken, "resetToken", errors);
            PasswordRules.Validate(request.Password, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var hash = SecureTokens.Hash(request.ResetToken!);
            var userId = await db.PasswordResetChallenges.AsNoTracking().Where(x => x.ResetTokenHash == hash)
                .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
            ApiRules.Require(userId != null, 400, InvalidToken);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId!.Value} FOR NO KEY UPDATE")
                .SingleOrDefaultAsync(ct);
            var challenge = await db.PasswordResetChallenges.SingleAsync(x => x.ResetTokenHash == hash, ct);
            var now = DateTimeOffset.UtcNow;
            ApiRules.Require(user is { IsActive: true } && challenge.UsedAt == null && challenge.ConfirmedAt != null &&
                challenge.ResetTokenExpiresAt > now && challenge.NormalizedEmail == user.NormalizedEmail, 400, InvalidToken);
            // Share refresh/logout family locks so a concurrent rotation cannot escape revocation.
            var families = await db.RefreshSessions.Where(x => x.UserId == userId.Value)
                .Select(x => x.FamilyId).Distinct().OrderBy(x => x).ToListAsync(ct);
            foreach (var family in families)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({family.ToString("N")}, 0))", ct);
            user!.PasswordHash = PasswordHashing.Hash(request.Password!);
            user.UpdatedAt = now;
            await db.RefreshSessions.Where(x => x.UserId == userId.Value && x.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), ct);
            await db.PasswordResetChallenges.Where(x => x.UserId == userId.Value && x.UsedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).Produces(204).ProducesValidationProblem().ProducesProblem(429);
        return app;
    }

    // Domain-separate password reset codes from email verification even with identical digits.
    private static string HashCode(string code, PasswordResetChallenge challenge, IConfiguration configuration) =>
        EmailVerificationCodes.Hash("password-reset:" + code, challenge.Id, challenge.UserId, configuration["Jwt:SigningKey"]!);

    private static bool MatchesCode(string code, PasswordResetChallenge challenge, IConfiguration configuration) =>
        EmailVerificationCodes.Matches("password-reset:" + code, challenge.Id, challenge.UserId,
            configuration["Jwt:SigningKey"]!, challenge.CodeHash);
}
