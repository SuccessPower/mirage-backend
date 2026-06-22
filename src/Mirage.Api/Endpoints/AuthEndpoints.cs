using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Domain.Entities;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Authentication");
        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapPost("/logout-all", LogoutAll).RequireAuthorization();
        group.MapPost("/change-password", ChangePassword).RequireAuthorization();
        group.MapGet("/sessions", GetSessions).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> Register(RegisterRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Length > 0) return EndpointHelpers.ValidationProblem(context, errors);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = request.Email.Trim(),
                UserName = request.Email.Trim()
            };
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return EndpointHelpers.ValidationProblem(context,
                    result.Errors.GroupBy(x => x.Code)
                        .ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()),
                    "Registration validation failed.");

            var roleResult = await userManager.AddToRoleAsync(user, MirageRoles.User);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to assign default role: {string.Join("; ", roleResult.Errors.Select(x => x.Description))}");

            db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth, request.City,
                request.Country, request.Denomination, request.Intent, request.Bio));
            await db.SaveChangesAsync(cancellationToken);
            var response = await IssueTokens(user, [MirageRoles.User], db, tokens, configuration, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ApiResults.Ok(context, response, "Registration completed successfully.");
        });
    }

    private static async Task<IResult> Login(LoginRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Invalid email or password.");
        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Invalid email or password.");
        }
        await userManager.ResetAccessFailedCountAsync(user);
        var roles = await userManager.GetRolesAsync(user);
        return ApiResults.Ok(context,
            await IssueTokens(user, roles, db, tokens, configuration, cancellationToken),
            "Login completed successfully.");
    }

    private static async Task<IResult> Refresh(RefreshRequest request, HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, TokenService tokens, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var hash = RefreshToken.ComputeHash(request.RefreshToken);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(
            x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow,
            cancellationToken);
        if (existing is null)
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Invalid refresh token.");
        existing.Revoke();
        var user = await userManager.FindByIdAsync(existing.UserId.ToString());
        if (user is null || !user.IsActive)
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "User is unavailable.");
        var roles = await userManager.GetRolesAsync(user);
        return ApiResults.Ok(context,
            await IssueTokens(user, roles, db, tokens, configuration, cancellationToken),
            "Token refreshed successfully.");
    }

    private static async Task<IResult> Logout(RefreshRequest request, HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var hash = RefreshToken.ComputeHash(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(
            x => x.UserId == userId && x.TokenHash == hash && x.RevokedAt == null,
            cancellationToken);
        if (token is null) return EndpointHelpers.NotFound(context, "Active session was not found.");

        token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { token.Id }, "Logout completed successfully.");
    }

    private static async Task<IResult> LogoutAll(HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { revokedSessions = tokens.Count },
            "All sessions have been logged out successfully.");
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return EndpointHelpers.ValidationProblem(context,
                ("password", "Current and new passwords are required."));

        var user = await userManager.FindByIdAsync(context.User.GetUserId().ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User account was not found.");
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return EndpointHelpers.ValidationProblem(context,
                result.Errors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()),
                "Password change failed.");

        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == user.Id && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { revokedSessions = tokens.Count },
            "Password changed successfully. Sign in again on all devices.");
    }

    private static async Task<IResult> GetSessions(HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var sessions = await db.RefreshTokens.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AccountSessionResponse(
                x.Id,
                x.CreatedAt,
                x.ExpiresAt,
                x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow))
            .Take(100)
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, sessions, "Account sessions retrieved successfully.");
    }

    private static async Task<AuthResponse> IssueTokens(ApplicationUser user, IEnumerable<string> roles,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var access = tokens.CreateAccessToken(user, roles);
        var refreshValue = tokens.CreateRefreshToken();
        var refreshDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);
        db.RefreshTokens.Add(new RefreshToken(user.Id, refreshValue, DateTimeOffset.UtcNow.AddDays(refreshDays)));
        await db.SaveChangesAsync(cancellationToken);
        return new AuthResponse(access.Token, access.ExpiresAt, refreshValue);
    }

    private static (string Field, string Error)[] Validate(RegisterRequest request)
    {
        var errors = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(request.Email)) errors.Add(("email", "Email is required."));
        if (string.IsNullOrWhiteSpace(request.DisplayName)) errors.Add(("displayName", "Display name is required."));
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))
            errors.Add(("dateOfBirth", "Users must be at least 18 years old."));
        if (string.IsNullOrWhiteSpace(request.City)) errors.Add(("city", "City is required."));
        return errors.ToArray();
    }
}
