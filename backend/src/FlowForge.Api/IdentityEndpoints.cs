using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", async (RegisterRequestDto request, AuthService auth, CancellationToken cancellationToken) =>
        {
            var validation = Validate(request.Email, request.Password, request.DisplayName);
            if (validation is not null) return validation;
            try
            {
                var tokens = await auth.RegisterAsync(request.Email!, request.Password!, request.DisplayName!, cancellationToken);
                return Results.Created("/api/v1/auth/me", ToResponse(tokens));
            }
            catch (DuplicateEmailException)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Email already registered.");
            }
        }).RequireRateLimiting("register")
            .Produces<AuthTokenResponseDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (LoginRequestDto request, AuthService auth, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Email and password are required.");
            var tokens = await auth.LoginAsync(request.Email, request.Password, cancellationToken);
            return tokens is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.")
                : Results.Ok(ToResponse(tokens));
        }).RequireRateLimiting("login")
            .Produces<AuthTokenResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (RefreshTokenRequestDto request, AuthService auth, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Refresh token is required.");
            var tokens = await auth.RefreshAsync(request.RefreshToken, cancellationToken);
            return tokens is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token.")
                : Results.Ok(ToResponse(tokens));
        }).RequireRateLimiting("refresh")
            .Produces<AuthTokenResponseDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (RefreshTokenRequestDto request, AuthService auth, CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
                await auth.LogoutAsync(request.RefreshToken, cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("refresh")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (HttpContext context, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var id = Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var user = await db.Users.AsNoTracking().Where(candidate => candidate.Id == id && candidate.IsActive)
                .Select(candidate => new CurrentUserResponseDto(candidate.Id, candidate.Email, candidate.DisplayName))
                .SingleOrDefaultAsync(cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireAuthorization()
            .Produces<CurrentUserResponseDto>()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult? Validate(string? email, string? password, string? displayName)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !new EmailAddressAttribute().IsValid(email.Trim()))
            errors["email"] = ["A valid email address of at most 320 characters is required."];
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
            errors["displayName"] = ["Display name must be between 1 and 100 characters."];
        if (password is null || password.Length < 12 || password.Length > 128)
            errors["password"] = ["Password must be between 12 and 128 characters."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static AuthTokenResponseDto ToResponse(AuthTokens tokens) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken);
}
