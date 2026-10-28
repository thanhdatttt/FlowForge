using System.IdentityModel.Tokens.Jwt;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Validations.Identity;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

// Receives DTOs, validates requests, calls the auth service, and returns response DTOs and HTTP errors.
public static class IdentityEndpoints
{
    private const string SessionCookie = "flowforge.refresh";

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", async (RegisterRequestDto request, HttpContext context, AuthService auth, CancellationToken cancellationToken) =>
        {
            var errors = RegisterRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            try
            {
                var tokens = await auth.RegisterAsync(request.Email!, request.Password!, request.DisplayName!, cancellationToken);
                SetSessionCookie(context, tokens.RefreshToken);
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

        group.MapPost("/login", async (LoginRequestDto request, HttpContext context, AuthService auth, CancellationToken cancellationToken) =>
        {
            var errors = LoginRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var tokens = await auth.LoginAsync(request.Email!, request.Password!, cancellationToken);
            if (tokens is not null) SetSessionCookie(context, tokens.RefreshToken);
            return tokens is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.")
                : Results.Ok(ToResponse(tokens));
        }).RequireRateLimiting("login")
            .Produces<AuthTokenResponseDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/session", async (HttpContext context, AuthService auth, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.Cookies.TryGetValue(SessionCookie, out var token))
                return Results.Unauthorized();
            var tokens = await auth.RefreshAsync(token, cancellationToken);
            if (tokens is null)
            {
                context.Response.Cookies.Delete(SessionCookie, CookieOptions(context));
                return Results.Unauthorized();
            }
            SetSessionCookie(context, tokens.RefreshToken);
            return Results.Ok(ToResponse(tokens));
        }).RequireRateLimiting("refresh")
            .Produces<AuthTokenResponseDto>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (RefreshTokenRequestDto request, HttpContext context, AuthService auth, CancellationToken cancellationToken) =>
        {
            var errors = RefreshTokenRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var token = BrowserToken(context, request.RefreshToken!);
            // A different tab may have signed into a new account/session. Do not adopt it.
            if (!await auth.IsSameSessionFamilyAsync(token, request.RefreshToken!, cancellationToken))
                return Results.Unauthorized();
            var tokens = await auth.RefreshAsync(token, cancellationToken);
            if (tokens is not null) SetSessionCookie(context, tokens.RefreshToken);
            else if (context.Request.Headers["X-FlowForge-Session"] == "browser")
                context.Response.Cookies.Delete(SessionCookie, CookieOptions(context));
            return tokens is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token.")
                : Results.Ok(ToResponse(tokens));
        }).RequireRateLimiting("refresh")
            .Produces<AuthTokenResponseDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (RefreshTokenRequestDto request, HttpContext context, AuthService auth, CancellationToken cancellationToken) =>
        {
            var errors = RefreshTokenRequestValidator.Validate(request, allowEmpty: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var token = BrowserToken(context, request.RefreshToken ?? "");
            if (!string.IsNullOrWhiteSpace(request.RefreshToken) &&
                !await auth.IsSameSessionFamilyAsync(token, request.RefreshToken, cancellationToken))
            {
                // Revoke the requesting tab's old session, preserving the newer cookie.
                await auth.LogoutAsync(request.RefreshToken, cancellationToken);
                return Results.NoContent();
            }
            if (!string.IsNullOrWhiteSpace(token))
                await auth.LogoutAsync(token, cancellationToken);
            context.Response.Cookies.Delete(SessionCookie, CookieOptions(context));
            context.Response.Headers.CacheControl = "no-store";
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

    // Browser tabs share a rotating cookie; JSON-token clients retain their existing contract.
    private static string BrowserToken(HttpContext context, string fallback) =>
        context.Request.Headers["X-FlowForge-Session"] == "browser" &&
        context.Request.Cookies.TryGetValue(SessionCookie, out var token) ? token : fallback;

    private static CookieOptions CookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps || !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        IsEssential = true
    };

    private static void SetSessionCookie(HttpContext context, string token)
    {
        var options = CookieOptions(context);
        options.MaxAge = TimeSpan.FromDays(30);
        context.Response.Cookies.Append(SessionCookie, token, options);
        context.Response.Headers.CacheControl = "no-store";
    }
}
