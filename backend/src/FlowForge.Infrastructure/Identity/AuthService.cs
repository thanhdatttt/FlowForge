using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FlowForge.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace FlowForge.Infrastructure.Identity;

public sealed record AuthTokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

public sealed class DuplicateEmailException : Exception;

// Implements registration, login, token issuance, refresh rotation, and logout
public sealed class AuthService(FlowForgeDbContext db, IConfiguration configuration)
{
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    // register
    public async Task<AuthTokens> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = new User
        {
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = displayName.Trim(),
            PasswordHash = PasswordHashing.Hash(password)
        };
        db.Users.Add(user);
        var (session, token) = NewSession(user.Id, Guid.NewGuid());
        db.RefreshSessions.Add(session);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DuplicateEmailException();
        }
        return CreateTokens(user, token);
    }

    // login
    public async Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        // Serialize credential checking/session creation with password reset. NO KEY UPDATE
        // still permits refresh-session FK checks while reset holds the account lock.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var normalizedEmail = NormalizeEmail(email);
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM users WHERE normalized_email = {normalizedEmail} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || !user.IsActive || !PasswordHashing.Verify(password, user.PasswordHash)) return null;

        var (session, token) = NewSession(user.Id, Guid.NewGuid());
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreateTokens(user, token);
    }

    // Refresh, reuse detection, and logout share a transaction lock per token family.
    // This prevents a concurrent logout/replay snapshot from missing a new replacement.
    public async Task<AuthTokens?> RefreshAsync(string token, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        var familyId = await db.RefreshSessions.AsNoTracking().Where(session => session.TokenHash == hash)
            .Select(session => (Guid?)session.FamilyId).SingleOrDefaultAsync(cancellationToken);
        if (familyId == null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(familyId.Value, cancellationToken);
        // Re-read only after the lock: the token may have been rotated or revoked while waiting.
        var current = await db.RefreshSessions.AsNoTracking().Include(session => session.User)
            .SingleAsync(session => session.TokenHash == hash, cancellationToken);
        if (current.RevokedAt is not null)
        {
            await RevokeFamilyAsync(current.FamilyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        if (current.ExpiresAt <= DateTimeOffset.UtcNow || !current.User.IsActive) return null;

        var (replacement, replacementToken) = NewSession(current.UserId, current.FamilyId);
        db.RefreshSessions.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        await db.RefreshSessions.Where(session => session.Id == current.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(session => session.ReplacedById, replacement.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreateTokens(current.User, replacementToken);
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        var familyId = await db.RefreshSessions.AsNoTracking().Where(session => session.TokenHash == hash)
            .Select(session => (Guid?)session.FamilyId).SingleOrDefaultAsync(cancellationToken);
        if (familyId == null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(familyId.Value, cancellationToken);
        await RevokeFamilyAsync(familyId.Value, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task LockFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({familyId.ToString("N")}, 0))", cancellationToken);

    // revoke token
    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        await db.RefreshSessions.Where(session => session.FamilyId == familyId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    // helpers
    // access token
    private AuthTokens CreateTokens(User user, string refreshToken)
    {
        var expires = DateTimeOffset.UtcNow.Add(AccessLifetime);
        var key = Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"]!);
        var jwt = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "flowforge",
            audience: configuration["Jwt:Audience"] ?? "flowforge-api",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        return new AuthTokens(new JwtSecurityTokenHandler().WriteToken(jwt), expires, refreshToken);
    }

    // refresh token
    private static (RefreshSession Session, string Token) NewSession(Guid userId, Guid familyId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (new RefreshSession
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = HashToken(token),
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshLifetime)
        }, token);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
