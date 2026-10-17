using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.Workspaces;
using FlowForge.Api.Validations;
using FlowForge.Api.Validations.Identity;
using FlowForge.Api.Validations.Projects;
using FlowForge.Api.Validations.Workspaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class RequestValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Workspace_names_are_required_for_creation_and_update(string? name)
    {
        Assert.Contains("name", CreateWorkspaceRequestValidator.Validate(new(name)).Keys);
        Assert.Contains("name", UpdateWorkspaceRequestValidator.Validate(new(name)).Keys);
    }

    [Fact]
    public void Workspace_names_use_trimmed_length_boundaries()
    {
        Assert.Empty(CreateWorkspaceRequestValidator.Validate(new(" " + new string('x', 100) + " ")));
        Assert.Contains("name", UpdateWorkspaceRequestValidator.Validate(new(new string('x', 101))).Keys);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("Admin")]
    [InlineData("Manager")]
    [InlineData("Developer")]
    [InlineData("Viewer")]
    public void Role_syntax_accepts_named_roles_independently_of_assignment_permissions(string role)
    {
        Assert.Empty(ChangeWorkspaceRoleRequestValidator.Validate(new(role)));
        Assert.Empty(CreateInvitationRequestValidator.Validate(new("user@example.com", role)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("Unknown")]
    [InlineData(" Admin ")]
    public void Numeric_and_unknown_roles_are_rejected(string? role)
    {
        Assert.Contains("role", ChangeWorkspaceRoleRequestValidator.Validate(new(role)).Keys);
        Assert.Contains("role", AddWorkspaceMemberRequestValidator.Validate(new("user@example.com", role)).Keys);
    }

    [Fact]
    public void Invitations_and_membership_requests_report_email_and_role_errors_together()
    {
        var invitation = CreateInvitationRequestValidator.Validate(new("invalid", "Unknown"));
        var member = AddWorkspaceMemberRequestValidator.Validate(new("invalid", "Unknown"));
        Assert.Equal(new[] { "email", "role" }, invitation.Keys.Order().ToArray());
        Assert.Equal(new[] { "email", "role" }, member.Keys.Order().ToArray());
        Assert.Contains("email", CreateInvitationRequestValidator.Validate(new(new string('x', 321), "Developer")).Keys);
    }

    [Fact]
    public void Empty_member_and_owner_identifiers_are_reported_on_the_correct_fields()
    {
        Assert.Contains("userId", ProjectMemberRequestValidator.Validate(new(Guid.Empty)).Keys);
        Assert.Contains("newOwnerUserId", TransferOwnershipRequestValidator.Validate(new(Guid.Empty)).Keys);
        Assert.Empty(ProjectMemberRequestValidator.Validate(new(Guid.NewGuid())));
        Assert.Empty(TransferOwnershipRequestValidator.Validate(new(Guid.NewGuid())));
    }

    [Fact]
    public void Project_validation_collects_all_invalid_fields()
    {
        var errors = CreateProjectRequestValidator.Validate(new("", "!", new string('x', 4001), Guid.Empty,
            new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 1)));
        Assert.Equal(new[] { "description", "leadUserId", "name", "projectKey", "targetOn" }, errors.Keys.Order().ToArray());
        Assert.Contains("version", UpdateProjectRequestValidator.Validate(new("Valid", "FLOW", null, null, null, null, 0)).Keys);
        Assert.Empty(UpdateProjectRequestValidator.Validate(new("Valid", "FLOW", null, null, null, null, 1)));
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("a1")]
    [InlineData("ABCDEFGHIJ")]
    public void Valid_project_key_boundaries_and_same_day_dates_are_accepted(string key)
    {
        var date = new DateOnly(2026, 10, 16);
        Assert.Empty(CreateProjectRequestValidator.Validate(new(new string('x', 100), key, new string('x', 4000), null, date, date)));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("1AB")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("FLOW-KEY")]
    public void Invalid_project_keys_are_rejected(string key) =>
        Assert.Contains("projectKey", CreateProjectRequestValidator.Validate(new("Valid", key)).Keys);

    [Theory]
    [InlineData(null, null)]
    [InlineData(1, 1)]
    [InlineData(1000000, 100)]
    public void Pagination_accepts_defaults_and_boundaries(int? page, int? pageSize) =>
        Assert.Empty(PaginationValidator.Validate(page, pageSize));

    [Fact]
    public void Query_validation_reports_pagination_search_sort_and_identifier_errors()
    {
        var errors = ProjectListQueryValidator.Validate(new string('x', 101), "unsafe", Guid.Empty, 0, 101);
        Assert.Equal(new[] { "leadUserId", "page", "pageSize", "search", "sort" }, errors.Keys.Order().ToArray());
        Assert.Contains("page", PaginationValidator.Validate(1000001, null).Keys);
        Assert.Contains("pageSize", PaginationValidator.Validate(null, 0).Keys);
        foreach (var sort in new[] { "name", "key", "createdAt", "updatedAt" })
            Assert.Empty(ProjectListQueryValidator.Validate(null, sort, null, null, null));
    }

    [Fact]
    public void Identity_validation_does_not_apply_registration_complexity_to_login()
    {
        Assert.Empty(LoginRequestValidator.Validate(new("user@example.com", "legacy-password")));
        Assert.Contains("email", LoginRequestValidator.Validate(new("invalid", "password")).Keys);
        Assert.Contains("password", LoginRequestValidator.Validate(new("user@example.com", new string('x', 129))).Keys);
        Assert.Contains("refreshToken", RefreshTokenRequestValidator.Validate(new(null)).Keys);
        Assert.Empty(RefreshTokenRequestValidator.Validate(new(null), allowEmpty: true));
        Assert.Contains("refreshToken", RefreshTokenRequestValidator.Validate(new(new string('x', 257)), allowEmpty: true).Keys);
        Assert.Contains("token", InvitationTokenRequestValidator.Validate(new(new string('x', 257))).Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("１２３４５６")]
    public void Otp_requires_six_ascii_digits(string? code) =>
        Assert.Contains("code", EmailVerificationRequestValidator.Validate(new(code)).Keys);

    [Fact]
    public void Otp_accepts_leading_zeros() => Assert.Empty(EmailVerificationRequestValidator.Validate(new("000123")));

    [Fact]
    public async Task Identity_endpoints_return_field_errors_before_accessing_the_database()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:FlowForge", "Host=localhost;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Jwt:SigningKey", "validation-test-signing-key-at-least-32-bytes");
        });
        using var client = factory.CreateClient();
        foreach (var (route, body, field) in new (string Route, object Body, string Field)[]
        {
            ("login", new LoginRequestDto("invalid", "password"), "email"),
            ("refresh", new RefreshTokenRequestDto(null), "refreshToken"),
            ("logout", new RefreshTokenRequestDto(new string('x', 257)), "refreshToken")
        })
        {
            var response = await client.PostAsJsonAsync($"/api/v1/auth/{route}", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshTokenRequestDto(null))).StatusCode);
    }
}
