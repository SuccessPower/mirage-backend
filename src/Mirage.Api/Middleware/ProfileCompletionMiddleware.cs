using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Middleware;

/// <summary>
/// Server-side onboarding boundary. The UI modal is only presentation; this middleware ensures
/// an authenticated client cannot bypass mandatory profile/photo completion by calling APIs
/// directly with a token issued during Google onboarding.
/// </summary>
public sealed class ProfileCompletionMiddleware(RequestDelegate next)
{
    private static readonly PathString[] AllowedPaths =
    [
        "/api/v1/profiles/me",
        "/api/v1/profiles/me/complete",
        "/api/v1/upload/sign",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",
        "/api/v1/auth/logout-all",
        // Accepting a Platform Manager invitation is what grants the role that exempts the caller from
        // onboarding, so the accept call itself must not be gated behind profile completion.
        "/api/v1/newsletters/platform-manager-invites/accept",
        "/api/v1/newsletters/unsubscribe"
    ];

    public async Task InvokeAsync(HttpContext context, MirageDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || context.User.IsInRole("PlatformAdmin")
            || context.User.IsInRole("PlatformManager")
            || AllowedPaths.Any(path => context.Request.Path.Equals(path)))
        {
            await next(context);
            return;
        }

        var subject = context.User.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var userId))
        {
            await next(context);
            return;
        }

        var complete = await db.Profiles.AsNoTracking()
            .AnyAsync(profile => profile.UserId == userId && profile.IsProfileComplete,
                context.RequestAborted);
        if (complete)
        {
            await next(context);
            return;
        }

        await Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Profile completion required",
            detail: "Complete your profile and upload a validated face photo before using Mirage.",
            extensions: new Dictionary<string, object?> { ["code"] = "profile_completion_required" })
            .ExecuteAsync(context);
    }
}
