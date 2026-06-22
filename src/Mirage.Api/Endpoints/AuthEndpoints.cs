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
        return api;
    }

    private static async Task<IResult> Register(RegisterRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Length > 0) return EndpointHelpers.ValidationProblem(context, errors);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = request.Email.Trim(), UserName = request.Email.Trim() };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return EndpointHelpers.ValidationProblem(context,
                result.Errors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()),
                "Registration validation failed.");

        await userManager.AddToRoleAsync(user, MirageRoles.User);
        db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth, request.City,
            request.Country, request.Denomination, request.Intent, request.Bio));
        await db.SaveChangesAsync(cancellationToken);
        var response = await IssueTokens(user, [MirageRoles.User], db, tokens, configuration, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ApiResults.Ok(context, response, "Registration completed successfully.");
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
