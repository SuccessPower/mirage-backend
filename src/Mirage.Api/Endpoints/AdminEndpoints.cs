using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
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
        admin.MapPost("/users/welcome-emails/backfill", BackfillWelcomeEmails);

        // Content reports
        admin.MapGet("/reports", ListReports);
        admin.MapGet("/reports/{id:guid}", GetReport);
        admin.MapPatch("/reports/{id:guid}/action", TakeAction);
        admin.MapPatch("/reports/{id:guid}/dismiss", DismissReport);

        // Independent counsellor verification (self-signup, no organisation)
        admin.MapGet("/counsellors", ListCounsellors);
        admin.MapGet("/counsellors/pending", ListPendingIndependentCounsellors);
        admin.MapPatch("/counsellors/{id:guid}/approve", ApproveIndependentCounsellor);
        admin.MapPatch("/counsellors/{id:guid}/reject", RejectIndependentCounsellor);
        admin.MapPatch("/counsellors/{id:guid}/approve-charging", ApproveCounsellorCharging);
        admin.MapPatch("/counsellors/{id:guid}/decline-charging", DeclineCounsellorCharging);

        // Mentor verification
        admin.MapGet("/mentors", ListMentorProfiles);
        admin.MapGet("/mentors/pending", ListPendingMentors);
        admin.MapPatch("/mentors/{id:guid}/approve", ApproveMentor);

        // Couples overview
        admin.MapGet("/couples", ListCouples);

        // Organisation admin invites — skips the Pending review queue when redeemed
        admin.MapPost("/organisations/invite", InviteOrganisationAdmin);

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
                x.CreatedAt,
                DisplayName = db.Profiles.Where(p => p.UserId == x.Id).Select(p => p.DisplayName).FirstOrDefault(),
                Roles = db.UserRoles.Where(ur => ur.UserId == x.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .ToList()
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
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == id, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        if (profile.IsVerified) return EndpointHelpers.Conflict(context, "Profile is already verified.");
        profile.Verify();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(id, NotificationType.ProfileVerified, "Your profile is verified",
            "your profile has been verified. Verified members get priority visibility in Discovery and can send date requests for any relationship intent.",
            cancellationToken: cancellationToken);

        return ApiResults.Ok(context, new { UserId = id, profile.IsVerified }, "Profile verified successfully.");
    }

    // Runs the backlog down in batches within one request rather than one giant query — caps
    // out at 20 batches (500 users) per call so the request can't run away; call again if
    // TotalSent hits the cap and more remain.
    private static async Task<IResult> BackfillWelcomeEmails(HttpContext context,
        WelcomeEmailBackfillService backfill, CancellationToken cancellationToken)
    {
        const int batchSize = 25;
        const int maxBatches = 20;
        var totalSent = 0;
        var batches = 0;
        int sentThisBatch;
        do
        {
            sentThisBatch = await backfill.RunBatchAsync(batchSize, cancellationToken);
            totalSent += sentThisBatch;
            batches++;
        } while (sentThisBatch > 0 && batches < maxBatches);

        return ApiResults.Ok(context, new { TotalSent = totalSent, ReachedBatchCap = batches >= maxBatches },
            "Welcome email backfill completed.");
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

    // --- Independent counsellor verification ---

    private static async Task<IResult> ListCounsellors(HttpContext context, IMirageDbContext db,
        bool? approved, bool? rejected, int page = 1, int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var query = db.Counsellors.AsNoTracking().AsQueryable();
        if (approved.HasValue) query = query.Where(x => x.IsApproved == approved.Value);
        if (rejected.HasValue) query = query.Where(x => x.IsRejected == rejected.Value);

        var result = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                DisplayName = x.UserProfile.DisplayName,
                x.UserProfile.City,
                x.UserProfile.Country,
                x.UserProfile.Denomination,
                x.OrganisationId,
                OrganisationName = x.Organisation != null ? x.Organisation.Name : null,
                x.YearsExperience,
                x.IsApproved,
                x.IsRejected,
                Status = x.IsApproved ? "Approved" : x.IsRejected ? "Rejected" : "Pending",
                x.RejectionReason,
                x.Specialisations,
                x.Languages,
                x.VerificationDocumentUrls,
                x.CreatedAt,
                x.AcceptsFreeSessions,
                x.CompletedFreeSessionsCount,
                IsEligibleToCharge = x.CompletedFreeSessionsCount >= CounsellorProfile.MinimumFreeSessionsBeforeCharging,
                x.ChargingRequested,
                x.AverageRating,
                x.RatingCount,
                TotalCompletedSessions = db.CounsellingSessions.Count(s => s.CounsellorId == x.Id && s.Status == SessionStatus.Completed),
                HasPayoutAccount = x.PaystackSubaccountCode != null || x.FlutterwaveSubaccountId != null
            })
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        return ApiResults.Ok(context, result, "Counsellors retrieved successfully.");
    }

    private static async Task<IResult> ApproveCounsellorCharging(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var counsellor = await db.Counsellors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Counsellor was not found.");
        try { counsellor.ApproveCharging(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { counsellor.Id, counsellor.AcceptsFreeSessions },
            "Counsellor is now approved to charge for sessions.");
    }

    private static async Task<IResult> DeclineCounsellorCharging(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var counsellor = await db.Counsellors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Counsellor was not found.");
        try { counsellor.DeclineChargingRequest(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { counsellor.Id, counsellor.ChargingRequested }, "Charging request declined.");
    }

    private static async Task<IResult> ListPendingIndependentCounsellors(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var counsellors = await db.Counsellors.AsNoTracking()
            .Where(x => x.OrganisationId == null && !x.IsApproved && !x.IsRejected)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                DisplayName = x.UserProfile.DisplayName,
                x.UserProfile.City,
                x.UserProfile.Country,
                x.YearsExperience,
                x.Specialisations,
                x.Languages,
                x.VerificationDocumentUrls,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, counsellors, "Pending independent counsellors retrieved successfully.");
    }

    private static async Task<IResult> ApproveIndependentCounsellor(Guid id, HttpContext context, IMirageDbContext db,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var counsellor = await db.Counsellors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Counsellor was not found.");
        if (counsellor.IsApproved) return EndpointHelpers.Conflict(context, "Counsellor is already approved.");
        counsellor.Approve();
        await db.SaveChangesAsync(cancellationToken);

        var user = await userManager.FindByIdAsync(counsellor.UserId.ToString());
        if (user is not null && !await userManager.IsInRoleAsync(user, MirageRoles.Counsellor))
            await userManager.AddToRoleAsync(user, MirageRoles.Counsellor);

        return ApiResults.Ok(context, new { counsellor.Id, counsellor.IsApproved }, "Counsellor approved successfully.");
    }

    private static async Task<IResult> RejectIndependentCounsellor(Guid id, RejectCounsellorRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return EndpointHelpers.ValidationProblem(context, ("reason", "A rejection reason is required."));
        var counsellor = await db.Counsellors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Counsellor was not found.");
        counsellor.Reject(request.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { counsellor.Id, counsellor.IsRejected }, "Counsellor rejected.");
    }

    // --- Mentor verification ---

    private static async Task<IResult> ListMentorProfiles(HttpContext context, IMirageDbContext db,
        bool? approved, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var query = db.Mentors.AsNoTracking().AsQueryable();
        if (approved.HasValue) query = query.Where(x => x.IsApproved == approved.Value);

        var result = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                DisplayName = x.UserProfile.DisplayName,
                x.UserProfile.City,
                x.UserProfile.Country,
                x.UserProfile.Denomination,
                x.YearsMarried,
                x.IsApproved,
                Status = x.IsApproved ? "Approved" : "Pending",
                x.AcceptsFreeSessions,
                x.AreasOfGuidance,
                x.Languages,
                x.CreatedAt
            })
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        return ApiResults.Ok(context, result, "Mentor profiles retrieved successfully.");
    }

    private static async Task<IResult> ListPendingMentors(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var mentors = await db.Mentors.AsNoTracking()
            .Where(x => !x.IsApproved)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                DisplayName = x.UserProfile.DisplayName,
                x.UserProfile.City,
                x.UserProfile.Country,
                x.UserProfile.Denomination,
                x.YearsMarried,
                x.AreasOfGuidance,
                x.Languages,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResults.Ok(context, mentors, "Pending mentors retrieved successfully.");
    }

    private static async Task<IResult> ApproveMentor(Guid id, HttpContext context, IMirageDbContext db,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var mentor = await db.Mentors.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (mentor is null) return EndpointHelpers.NotFound(context, "Mentor profile was not found.");
        if (mentor.IsApproved) return EndpointHelpers.Conflict(context, "Mentor profile is already approved.");

        mentor.Approve();
        var user = await userManager.FindByIdAsync(mentor.UserId.ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "Mentor user account was not found.");
        if (!await userManager.IsInRoleAsync(user, MirageRoles.Mentor))
            await userManager.AddToRoleAsync(user, MirageRoles.Mentor);

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { mentor.Id, mentor.UserId, mentor.IsApproved },
            "Mentor approved successfully.");
    }

    // --- Couples overview ---

    private static async Task<IResult> ListCouples(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var couples = await db.Couples.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.RequestedByUserId,
                x.CreatedAt,
                x.ReviewedAt,
                User1Name = db.Profiles.Where(p => p.UserId == x.User1Id).Select(p => p.DisplayName).FirstOrDefault(),
                User2Name = db.Profiles.Where(p => p.UserId == x.User2Id).Select(p => p.DisplayName).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, couples, "Couples retrieved successfully.");
    }

    // --- Organisation admin invites ---

    private static async Task<IResult> InviteOrganisationAdmin(InviteOrganisationAdminRequest request,
        HttpContext context, IMirageDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "A valid email address is required."));

        var normalizedEmail = request.Email.ToLowerInvariant().Trim();
        var existingPending = await db.OrganisationAdminInvites.AnyAsync(
            x => x.Email == normalizedEmail && x.RedeemedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow,
            cancellationToken);
        if (existingPending)
            return EndpointHelpers.Conflict(context, "An active invite already exists for this email address.");

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var expiryDays = configuration.GetValue("OrganisationAdminInvite:ExpiryDays", 14);
        db.OrganisationAdminInvites.Add(new OrganisationAdminInvite(normalizedEmail, rawToken,
            DateTimeOffset.UtcNow.AddDays(expiryDays)));
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context,
            new { Email = normalizedEmail, InviteToken = rawToken, ExpiresInDays = expiryDays },
            "Organisation admin invite created. Share the token with the invitee — their organisation will be pre-approved.");
    }
}
