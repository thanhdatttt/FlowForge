using System.Net;
using FlowForge.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class GmailSmtpEmailSenderTests
{
    [Theory]
    [InlineData("abcd efgh ijkl mnop")]
    [InlineData("  abcd\tefgh\nijkl\r\nmnop  ")]
    [InlineData("abcdefghijklmnop")]
    public void Gmail_credentials_accept_grouped_app_passwords_without_sending_formatting_whitespace(string password)
    {
        var sender = Sender(" sender@example.com  ", password);
        Assert.True(sender.IsConfigured);
        using var client = sender.CreateClient();
        var credentials = Assert.IsType<NetworkCredential>(client.Credentials);
        Assert.Equal("sender@example.com", credentials.UserName);
        Assert.Equal("abcdefghijklmnop", credentials.Password);
        Assert.Equal("smtp.gmail.com", client.Host);
        Assert.Equal(587, client.Port);
        Assert.True(client.EnableSsl);
        Assert.False(client.UseDefaultCredentials);
    }

    [Theory]
    [InlineData(null, "abcdefghijklmnop")]
    [InlineData(" ", "abcdefghijklmnop")]
    [InlineData("sender@example.com", null)]
    [InlineData("sender@example.com", " \t\r\n")]
    public async Task Missing_credentials_fail_before_attempting_delivery(string? username, string? password)
    {
        var sender = Sender(username, password);
        Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendVerificationAsync("recipient@example.invalid", "012345", 10, CancellationToken.None));
    }

    private static GmailSmtpEmailSender Sender(string? username, string? password) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Smtp:Username"] = username,
            ["Smtp:AppPassword"] = password
        }).Build(), NullLogger<GmailSmtpEmailSender>.Instance);
}
