using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Validation.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class RegisterRequestValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Aa1!abc")]
    [InlineData("abcdefgh")]
    [InlineData("abcdef1!")]
    [InlineData("ABCDEF1!")]
    [InlineData("Abcdefg!")]
    [InlineData("Abcdef12")]
    [InlineData("Abcdef1 ")]
    public void Rejects_short_or_weak_passwords(string? password)
    {
        var errors = RegisterRequestValidator.Validate(new("user@example.com", password, "Test User"));
        Assert.True(errors.ContainsKey("password"));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public void Accepts_strong_passwords_at_length_boundaries(int length)
    {
        var password = "Aa1!" + new string('x', length - 4);
        Assert.Empty(RegisterRequestValidator.Validate(new("user@example.com", password, "Test User")));
    }

    [Fact]
    public void Rejects_passwords_over_the_maximum_length()
    {
        var errors = RegisterRequestValidator.Validate(new("user@example.com", "Aa1!" + new string('x', 125), "Test User"));
        Assert.True(errors.ContainsKey("password"));
    }

    [Fact]
    public async Task Registration_returns_password_validation_errors_before_accessing_the_database()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:FlowForge",
                "Host=localhost;Database=flowforge;Username=flowforge;Password=test");
            builder.UseSetting("Jwt:SigningKey", "validation-test-signing-key-at-least-32-bytes");
        });
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto("user@example.com", "weakpass", "Test User"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("password", out _));
    }
}
