using System.Security.Cryptography;
using System.Text;

namespace FlowForge.Infrastructure.Identity;

public static class SecureTokens
{
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
