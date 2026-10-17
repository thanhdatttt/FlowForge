using System.IdentityModel.Tokens.Jwt;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Validation.Identity;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

// Receives DTOs, validates requests, calls the auth service, and returns response DTOs and HTTP errors.
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", async (RegisterRequestDto request, AuthService auth, CancellationToken cancellationToken) =>
        {
            var errors = RegisterRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
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
            var errors = LoginRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var tokens = await auth.LoginAsync(request.Email!, request.Password!, cancellationToken);
            return tokens is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.")
                : Results.Ok(ToResponse(tokens));
        }).RequireRateLimiting("login")
            .Produces<AuthTokenResponseDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (RefreshTokenRequestDto request, AuthService auth, CancellationToken cancellationToken) =>
        {
            var errors = RefreshTokenRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var tokens = await auth.RefreshAsync(request.RefreshToken!, cancellationToken);
            return tokens is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token.")
                : Results.Ok(ToResponse(tokens));
        }).RequireRateLimiting("refresh")
            .Produces<AuthTokenResponseDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (RefreshTokenRequestDto request, AuthService auth, CancellationToken cancellationToken) =>
        {
            var errors = RefreshTokenRequestValidator.Validate(request, allowEmpty: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
                await auth.LogoutAsync(request.RefreshToken, cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("refresh")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (HttpContext context, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var id = Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var user = await db.Users.AsNoTracking().Where(candidate => candidate.Id == id && candidate.IsActive)
                .Select(candidate => new CurrentUserResponseDto(candidate.Id, candidate.Email, candidate.DisplayName, candidate.EmailVerifiedAt != null))
                .SingleOrDefaultAsync(cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireAuthorization()
            .Produces<CurrentUserResponseDto>()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static AuthTokenResponseDto ToResponse(AuthTokens tokens) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken);
}
