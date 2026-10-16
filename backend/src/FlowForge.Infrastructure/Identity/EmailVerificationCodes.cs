using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FlowForge.Infrastructure.Identity;

// generate and verify OTP code
public static class EmailVerificationCodes
{
    public const int MaxFailedAttempts = 5;
    public static string Generate() => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    // Bind each low-entropy code to its user and challenge, using a server-held
    // secret so a database leak alone cannot reveal codes by enumerating digits.
    public static string Hash(string code, Guid challengeId, Guid userId, string signingKey) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey),
            Encoding.UTF8.GetBytes($"flowforge:email-verification:{challengeId:N}:{userId:N}:{code}")));

    public static bool Matches(string code, Guid challengeId, Guid userId, string signingKey, string expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(code, challengeId, userId, signingKey)),
            Encoding.ASCII.GetBytes(expectedHash));
}
