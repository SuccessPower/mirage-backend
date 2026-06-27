using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(MiragePolicy.PlatformAdmin);

        // User management
        admin.MapGet("/users", ListUsers);
        admin.MapGet("/users/{id:guid}", GetUser);
        admin.MapPatch("/users/{id:guid}/suspend", SuspendUser);
        admin.MapPatch("/users/{id:guid}/reactivate", ReactivateUser);
        admin.MapPatch("/users/{id:guid}/verify-profile", VerifyProfile);

        // Content reports
        admin.MapGet("/reports", ListReports);
        admin.MapGet("/reports/{id:guid}", GetReport);
        admin.MapPatch("/reports/{id:guid}/action", TakeAction);
        admin.MapPatch("/reports/{id:guid}/dismiss", DismissReport);

        // Content reports can also be submitted by any authenticated user
        var reports = api.MapGroup("/reports").WithTags("Reports").RequireAuthorization();
        reports.MapPost("/", SubmitReport);
        reports.MapGet("/mine", ListMyReports);

        return api;
    }

    // --- User management ---

    private static async Task<IResult> ListUsers(HttpContext context, MirageDbContext db,
        string? email, bool? isActive, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(email))
            query = query.Where(x => EF.Functions.ILike(x.Email!, $"%{email.Trim()}%"));
        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var result = query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Email,
                x.IsActive,
                x.EmailConfirmed,
                x.CreatedAt
            });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Users retrieved successfully.");
    }

    private static async Task<IResult> GetUser(Guid id, HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User was not found.");
        var roles = await userManager.GetRolesAsync(user);
        var profile = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == id)
            .Select(x => new { x.DisplayName, x.City, x.Country, x.Denomination, x.Intent, x.IsVerified, x.SubscriptionTier })
            .SingleOrDefaultAsync(cancellationToken);
        return ApiResults.Ok(context, new
        {
            user.Id,
            user.Email,
            user.IsActive,
            user.EmailConfirmed,
            user.CreatedAt,
            Roles = roles,
            Profile = profile
        }, "User retrieved successfully.");
    }

    private static async Task<IResult> SuspendUser(Guid id, HttpContext context,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var actor = context.User.GetUserId();
        if (actor == id) return EndpointHelpers.Conflict(context, "Cannot suspend your own account.");
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User was not found.");
        if (!user.IsActive) return EndpointHelpers.Conflict(context, "User is already suspended.");
        user.IsActive = false;
        await userManager.UpdateAsync(user);
        return ApiResults.Ok(context, new { UserId = id, IsActive = false }, "User suspended successfully.");
    }

    private static async Task<IResult> ReactivateUser(Guid id, HttpContext context,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User was not found.");
        if (user.IsActive) return EndpointHelpers.Conflict(context, "User is already active.");
        user.IsActive = true;
        await userManager.UpdateAsync(user);
        return ApiResults.Ok(context, new { UserId = id, IsActive = true }, "User reactivated successfully.");
    }

    private static async Task<IResult> VerifyProfile(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == id, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        if (profile.IsVerified) return EndpointHelpers.Conflict(context, "Profile is already verified.");
        profile.Verify();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { UserId = id, profile.IsVerified }, "Profile verified successfully.");
    }

    // --- Content reports ---

    private static async Task<IResult> ListReports(HttpContext context, IMirageDbContext db,
        ContentReportStatus? status, ContentReportTargetType? targetType,
        int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var query = db.ContentReports.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (targetType.HasValue) query = query.Where(x => x.TargetType == targetType.Value);

        var result = query.OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id, x.TargetType, x.TargetId, x.Reason, x.Details,
                x.Status, x.Resolution, x.ReportedByUserId, x.CreatedAt
            });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Content reports retrieved successfully.");
    }

    private static async Task<IResult> GetReport(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var report = await db.ContentReports.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.TargetType, x.TargetId, x.Reason, x.Details,
                x.Status, x.Resolution, x.ReportedByUserId, x.CreatedAt, x.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return report is null
            ? EndpointHelpers.NotFound(context, "Report was not found.")
            : ApiResults.Ok(context, report, "Report retrieved successfully.");
    }

    private static async Task<IResult> TakeAction(Guid id, ResolveReportRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Resolution))
            return EndpointHelpers.ValidationProblem(context, ("resolution", "Resolution is required."));
        var report = await db.ContentReports.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null) return EndpointHelpers.NotFound(context, "Report was not found.");
        if (report.Status == ContentReportStatus.ActionTaken)
            return EndpointHelpers.Conflict(context, "Action has already been taken on this report.");
        report.TakeAction(request.Resolution);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { report.Id, report.Status }, "Action taken on report.");
    }

    private static async Task<IResult> DismissReport(Guid id, ResolveReportRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Resolution))
            return EndpointHelpers.ValidationProblem(context, ("resolution", "Resolution note is required."));
        var report = await db.ContentReports.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null) return EndpointHelpers.NotFound(context, "Report was not found.");
        if (report.Status == ContentReportStatus.Dismissed)
            return EndpointHelpers.Conflict(context, "Report is already dismissed.");
        report.Dismiss(request.Resolution);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { report.Id, report.Status }, "Report dismissed.");
    }

    // --- Submitted by any authenticated user ---

    private static async Task<IResult> SubmitReport(SubmitContentReportRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (await db.ContentReports.AnyAsync(
                x => x.ReportedByUserId == userId && x.TargetId == request.TargetId &&
                     x.Status == ContentReportStatus.Pending, cancellationToken))
            return EndpointHelpers.Conflict(context, "You already have a pending report for this content.");

        var report = new ContentReport(userId, request.TargetType, request.TargetId, request.Reason, request.Details);
        db.ContentReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/reports/{report.Id}",
            new { report.Id, report.Status }, "Report submitted successfully.");
    }

    private static async Task<IResult> ListMyReports(HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var query = db.ContentReports.AsNoTracking()
            .Where(x => x.ReportedByUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.TargetType, x.TargetId, x.Reason, x.Status, x.CreatedAt });
        return ApiResults.Ok(context,
            await query.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Your reports retrieved successfully.");
    }
}
