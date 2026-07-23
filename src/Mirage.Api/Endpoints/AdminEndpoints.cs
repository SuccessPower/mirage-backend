using System.Security.Cryptography;
using System.Text.Json;
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
        admin.MapPost("/users/welcome-emails/reset", ResetWelcomeEmails);

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
        admin.MapGet("/couples/{id:guid}", GetCoupleDetail);

        // Organisation admin invites — skips the Pending review queue when redeemed
        admin.MapPost("/organisations/invite", InviteOrganisationAdmin);

        // Churches overview — every organisation regardless of Status, with membership/branch/admin
        // counts, for the platform-wide admin dashboard (separate from the ChurchAdmin's own view
        // of their single org in OrganisationEndpoints).
        admin.MapGet("/organisations", ListOrganisations);
        admin.MapPost("/organisations/seed-churches", SeedChurches);

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
            .Select(x => new { x.DisplayName, x.City, x.Country, x.Denomination, x.RelationshipStatus, x.IsVerified, x.SubscriptionTier })
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
            "your profile has been verified. Verified members get priority visibility in Discovery and can send date requests.",
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

    // Clears WelcomeEmailSentAt for specific users so the backfill endpoint will re-send to them —
    // needed once, for the batch of users whose welcome email was logged as "sent" by the old
    // Mailjet integration before we started validating the per-message Status in the response
    // body (it was returning HTTP 200 while silently dropping messages from an unverified sender).
    private static async Task<IResult> ResetWelcomeEmails(ResetWelcomeEmailsRequest request, HttpContext context,
        MirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.Emails is not { Length: > 0 })
            return EndpointHelpers.ValidationProblem(context, ("emails", "At least one email is required."));

        var normalized = request.Emails.Select(e => e.Trim().ToLowerInvariant()).ToArray();
        var updated = await db.Users
            .Where(x => x.Email != null && normalized.Contains(x.Email.ToLower()))
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.WelcomeEmailSentAt, (DateTimeOffset?)null), cancellationToken);

        return ApiResults.Ok(context, new { MatchedCount = updated },
            "Welcome email status reset — call the backfill endpoint to re-send.");
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

    private static async Task<IResult> GetCoupleDetail(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var couple = await db.Couples.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (couple is null) return EndpointHelpers.NotFound(context, "Couple was not found.");

        var profile1 = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == couple.User1Id, cancellationToken);
        var profile2 = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == couple.User2Id, cancellationToken);

        return ApiResults.Ok(context, new
        {
            couple.Id,
            couple.Status,
            couple.CreatedAt,
            couple.ReviewedAt,
            User1 = profile1?.ToResponse(false),
            User2 = profile2?.ToResponse(false)
        }, "Couple retrieved successfully.");
    }

    // --- Churches overview ---

    private static async Task<IResult> ListOrganisations(HttpContext context, MirageDbContext db,
        OrganisationStatus? status, string? query, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var orgsQuery = db.Organisations.AsNoTracking().AsQueryable();
        if (status.HasValue) orgsQuery = orgsQuery.Where(x => x.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var value = $"%{query.Trim()}%";
            orgsQuery = orgsQuery.Where(x => EF.Functions.ILike(x.Name, value) ||
                                             EF.Functions.ILike(x.Denomination, value));
        }

        var paged = await orgsQuery
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Denomination,
                x.Country,
                x.LogoUrl,
                x.WebsiteUrl,
                x.Status,
                x.AdminUserId,
                x.CreatedAt,
                AdminDisplayName = db.Profiles.Where(p => p.UserId == x.AdminUserId).Select(p => p.DisplayName).FirstOrDefault(),
                AdminEmail = db.Users.Where(u => u.Id == x.AdminUserId).Select(u => u.Email).FirstOrDefault(),
                ApprovedMemberCount = db.OrganisationMembers.Count(m => m.OrganisationId == x.Id && m.Status == OrganisationMemberStatus.Approved),
                PendingMemberCount = db.OrganisationMembers.Count(m => m.OrganisationId == x.Id && m.Status == OrganisationMemberStatus.Pending),
                BranchCount = db.OrganisationBranches.Count(b => b.OrganisationId == x.Id),
                ManagerCount = db.OrganisationManagers.Count(m => m.OrganisationId == x.Id)
            })
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        var response = new Mirage.Application.Common.PagedResult<AdminOrganisationSummaryResponse>(
            paged.Items.Select(x => new AdminOrganisationSummaryResponse(x.Id, x.Name, x.Denomination, x.Country,
                x.LogoUrl, x.WebsiteUrl, x.Status, x.AdminUserId, x.AdminDisplayName, x.AdminEmail, x.ApprovedMemberCount,
                x.PendingMemberCount, x.BranchCount, x.ManagerCount + 1, x.CreatedAt)).ToList(),
            paged.Page, paged.PageSize, paged.TotalCount);

        return ApiResults.Ok(context, response, "Churches retrieved successfully.");
    }

    // Churches curated out of the starter directory as no longer fitting the platform's
    // denomination lineup. Kept as a fixed list (rather than "anything missing from the JSON")
    // so re-seeding never touches an independently-created org that just isn't in the seed file.
    private static readonly string[] RetiredChurchNames =
    [
        "Celestial Church of Christ",
        "Cherubim and Seraphim Movement Church",
        "Brotherhood of the Cross and Star",
        "The Synagogue, Church of All Nations (SCOAN)"
    ];

    // Loads the curated starter directory (SeedData/nigerian-churches.json) and reconciles the
    // database against it in one pass: creates any church not already present by name
    // (pre-approved, owned by whichever PlatformAdmin runs this — real church admins are added
    // afterwards via the existing invite flow, since ownership can't be transferred once set),
    // corrects the denomination on existing rows whose JSON value has since changed, and retires
    // (suspends, not deletes — an Organisation may already have branches/members/events attached
    // with no cascade-delete path) any existing church dropped from the curated list. Safe to
    // re-run any time the JSON changes: insert/update/retire are all matched by exact name.
    private static async Task<IResult> SeedChurches(HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var actorId = context.User.GetUserId();
        var path = Path.Combine(AppContext.BaseDirectory, "SeedData", "nigerian-churches.json");
        if (!File.Exists(path))
            return EndpointHelpers.Problem(context, StatusCodes.Status500InternalServerError,
                "Seed data missing", "The church seed data file was not found on this deployment.");

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var entries = JsonSerializer.Deserialize<List<ChurchSeedEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var existingOrgs = await db.Organisations.ToListAsync(cancellationToken);
        var existingByName = new Dictionary<string, Organisation>(
            existingOrgs.ToDictionary(x => x.Name, x => x), StringComparer.OrdinalIgnoreCase);

        int created = 0, skipped = 0, branchesCreated = 0, denominationsUpdated = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;

            if (existingByName.TryGetValue(entry.Name, out var existing))
            {
                skipped++;
                if (!string.IsNullOrWhiteSpace(entry.Denomination) &&
                    !string.Equals(existing.Denomination, entry.Denomination, StringComparison.OrdinalIgnoreCase))
                {
                    existing.UpdateDenomination(entry.Denomination);
                    denominationsUpdated++;
                }
                continue;
            }

            var registrationNumber = $"SEED-{Guid.NewGuid():N}";
            var organisation = new Organisation(actorId, entry.Name, entry.Denomination ?? "Other",
                entry.HeadquartersCountry ?? "Nigeria", registrationNumber, entry.LogoUrl, entry.WebsiteUrl);
            organisation.Approve();
            db.Organisations.Add(organisation);

            foreach (var branch in entry.Branches ?? [])
            {
                if (string.IsNullOrWhiteSpace(branch.Name) || string.IsNullOrWhiteSpace(branch.City)) continue;
                db.OrganisationBranches.Add(new OrganisationBranch(organisation.Id, branch.Name, branch.City,
                    entry.HeadquartersCountry ?? "Nigeria", null));
                branchesCreated++;
            }

            existingByName.Add(entry.Name, organisation);
            created++;
        }

        var retired = 0;
        foreach (var org in existingOrgs)
        {
            if (RetiredChurchNames.Contains(org.Name, StringComparer.OrdinalIgnoreCase) &&
                org.Status != OrganisationStatus.Suspended)
            {
                org.Suspend();
                retired++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context,
            new { Created = created, Skipped = skipped, BranchesCreated = branchesCreated, DenominationsUpdated = denominationsUpdated, Retired = retired },
            $"Seeded {created} church(es) with {branchesCreated} branch(es); updated {denominationsUpdated} denomination(s); retired {retired}; skipped {skipped} already present.");
    }

    private sealed record ChurchSeedEntry(
        string Name,
        string? Denomination,
        string? HeadquartersCity,
        string? HeadquartersCountry,
        string? LeadPastor,
        string? LogoUrl,
        string? WebsiteUrl,
        List<ChurchSeedBranch>? Branches);

    private sealed record ChurchSeedBranch(string Name, string City);

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
