using FlowForge.Infrastructure.Identity;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PasswordHashingTests
{
    [Fact]
    public void Hash_is_salted_and_verifies_only_the_original_password()
    {
        const string password = "a-long-test-password";
        var first = PasswordHashing.Hash(password);
        var second = PasswordHashing.Hash(password);

        Assert.NotEqual(first, second);
        Assert.True(PasswordHashing.Verify(password, first));
        Assert.False(PasswordHashing.Verify("wrong-password", first));
        Assert.False(PasswordHashing.Verify(password, "malformed"));
    }
}
