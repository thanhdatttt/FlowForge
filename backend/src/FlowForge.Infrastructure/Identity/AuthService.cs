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

public sealed class AuthService(FlowForgeDbContext db, IConfiguration configuration)
{
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

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

    public async Task<AuthTokens?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == NormalizeEmail(email), cancellationToken);
        if (user is null || !user.IsActive || !PasswordHashing.Verify(password, user.PasswordHash)) return null;

        var (session, token) = NewSession(user.Id, Guid.NewGuid());
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return CreateTokens(user, token);
    }

    public async Task<AuthTokens?> RefreshAsync(string token, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        var current = await db.RefreshSessions.AsNoTracking().Include(session => session.User)
            .SingleOrDefaultAsync(session => session.TokenHash == hash, cancellationToken);
        if (current is null) return null;
        if (current.RevokedAt is not null)
        {
            await RevokeFamilyAsync(current.FamilyId, cancellationToken);
            return null;
        }
        if (current.ExpiresAt <= DateTimeOffset.UtcNow || !current.User.IsActive) return null;

        var (replacement, replacementToken) = NewSession(current.UserId, current.FamilyId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.RefreshSessions.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        var updated = await db.RefreshSessions.Where(session => session.Id == current.Id && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.RevokedAt, DateTimeOffset.UtcNow)
                .SetProperty(session => session.ReplacedById, replacement.Id), cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeFamilyAsync(current.FamilyId, cancellationToken);
            return null;
        }
        await transaction.CommitAsync(cancellationToken);
        return CreateTokens(current.User, replacementToken);
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        var session = await db.RefreshSessions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);
        if (session is not null) await RevokeFamilyAsync(session.FamilyId, cancellationToken);
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        await db.RefreshSessions.Where(session => session.FamilyId == familyId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

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
