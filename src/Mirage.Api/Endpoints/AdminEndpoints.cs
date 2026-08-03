using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private static int _adminReadCacheVersion;
    private static readonly TimeSpan AdminReadCacheDuration = TimeSpan.FromSeconds(30);

    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(MiragePolicy.PlatformAdmin);

        // User management
        admin.MapGet("/users", ListUsers);
        admin.MapGet("/users/{id:guid}", GetUser);
        admin.MapPatch("/users/{id:guid}/suspend", SuspendUser);
        admin.MapPatch("/users/{id:guid}/reactivate", ReactivateUser);
        admin.MapPatch("/users/{id:guid}/verify-profile", VerifyProfile);
        admin.MapPost("/users/{id:guid}/information-request", SendInformationRequest);
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

        // Provider-managed counsellor disbursements. Mirage records the commercial split and
        // approval audit trail; funds move through the licensed payment provider.
        admin.MapGet("/payouts", ListPayouts);
        admin.MapPost("/payouts/{id:guid}/approve", ApprovePayout);

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
        admin.MapPost("/organisations/{sourceId:guid}/merge", MergeOrganisation);

        // Content reports can also be submitted by any authenticated user
        var reports = api.MapGroup("/reports").WithTags("Reports").RequireAuthorization();
        reports.MapPost("/", SubmitReport);
        reports.MapGet("/mine", ListMyReports);

        return api;
    }

    private static async Task<IResult> ListPayouts(HttpContext context, MirageDbContext db,
        PayoutStatus? status, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = db.Payments.AsNoTracking().Where(x => x.Status == PaymentStatus.Successful);
        if (status.HasValue) query = query.Where(x => x.PayoutStatus == status.Value);
        var rows = query.OrderByDescending(x => x.UpdatedAt).Select(x => new
        {
            x.Id,
            x.CounsellingSessionId,
            x.CounsellorId,
            CounsellorName = x.CounsellingSession.Counsellor.UserProfile.DisplayName,
            x.Amount,
            x.PlatformFeeAmount,
            x.CounsellorAmount,
            x.Currency,
            x.Provider,
            x.PayoutStatus,
            x.PayoutReference,
            x.ProviderTransferId,
            x.PayoutApprovedByUserId,
            x.PayoutApprovedAt,
            x.PayoutPaidAt,
            x.PayoutFailureReason,
            SessionCompletedAt = x.CounsellingSession.UpdatedAt
        });
        return ApiResults.Ok(context, await rows.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Payouts retrieved successfully.");
    }

    private static async Task<IResult> ApprovePayout(Guid id, HttpContext context, MirageDbContext db,
        PaystackService paystack, FlutterwaveService flutterwave, ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.Include(x => x.CounsellingSession).ThenInclude(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (payment is null) return EndpointHelpers.NotFound(context, "Payout was not found.");
        if (payment.CounsellingSession.Status != SessionStatus.Completed)
            return EndpointHelpers.Conflict(context, "The counselling session is not complete.");

        var counsellor = payment.CounsellingSession.Counsellor;
        if (string.IsNullOrWhiteSpace(counsellor.BankCode) || string.IsNullOrWhiteSpace(counsellor.BankAccountNumber)
            || string.IsNullOrWhiteSpace(counsellor.BankAccountName))
            return EndpointHelpers.Conflict(context, "The counsellor has no verified payout account.");
        if (payment.Provider == PaymentProvider.Paystack
            && string.IsNullOrWhiteSpace(counsellor.PaystackTransferRecipientCode))
            return EndpointHelpers.Conflict(context, "The Paystack payout recipient is not configured.");

        try { payment.ApprovePayout(context.User.GetUserId()); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }

        // Persist the stable payout reference before the external call. Provider retries reuse it,
        // preventing duplicate credit if the HTTP response is lost after provider acceptance.
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            PayoutSubmissionResult result;
            if (payment.Provider == PaymentProvider.Paystack)
            {
                result = await paystack.InitiateTransferAsync(payment, counsellor.PaystackTransferRecipientCode!,
                    cancellationToken);
            }
            else
            {
                result = await flutterwave.InitiateTransferAsync(payment, counsellor.BankCode,
                    counsellor.BankAccountNumber, counsellor.BankAccountName, cancellationToken);
            }

            payment.MarkPayoutSubmitted(result.ProviderTransferId);
            if (result.Completed) payment.MarkPayoutPaid(result.ProviderTransferId);
            await db.SaveChangesAsync(cancellationToken);
            return ApiResults.Ok(context, new { payment.Id, payment.PayoutStatus, payment.PayoutReference },
                result.Completed ? "Payout completed." : "Payout submitted and awaiting provider confirmation.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payout submission failed for PaymentId {PaymentId} Reference {Reference}",
                payment.Id, payment.PayoutReference);
            payment.MarkPayoutFailed("Provider rejected or could not accept the payout request.");
            await db.SaveChangesAsync(cancellationToken);
            return EndpointHelpers.Problem(context, StatusCodes.Status502BadGateway,
                "Payout provider error", "The payout was not completed. It can be retried with the same reference.");
        }
    }

    // --- User management ---

    private static async Task<IResult> ListUsers(HttpContext context, MirageDbContext db, IMemoryCache cache,
        string? email, bool? isActive, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var cacheKey =
            $"admin:users:v{Volatile.Read(ref _adminReadCacheVersion)}:{normalizedEmail}:{isActive}:{page}:{pageSize}";
        var cached = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AdminReadCacheDuration;

            var query = db.Users.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(email))
                query = query.Where(x => EF.Functions.ILike(x.Email!, $"%{email.Trim()}%"));
            if (isActive.HasValue)
                query = query.Where(x => x.IsActive == isActive.Value);

            // Profile summary fields are embedded here (rather than left for the admin UI to fetch
            // per-row via GET /profiles/{id}) so listing N users costs one query, not N+1 — the prior
            // per-row fetch pattern could exhaust the DB connection pool under the admin page's own
            // concurrent Promise.all and made "profile unavailable" a frequent, misleading UI state.
            var result = query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.IsActive,
                    x.EmailConfirmed,
                    x.CreatedAt,
                    Profile = db.Profiles.Where(p => p.UserId == x.Id)
                        .Select(p => new
                        {
                            p.DisplayName,
                            p.AvatarUrl,
                            p.Occupation,
                            p.City,
                            p.Country,
                            p.RelationshipStatus,
                            p.IsVerified,
                            p.IsProfileComplete,
                            IsRecommended = db.Recommendations.Any(r =>
                                r.RecommendedUserId == x.Id && r.Status == RecommendationStatus.Active)
                        })
                        .FirstOrDefault(),
                    Roles = db.UserRoles.Where(ur => ur.UserId == x.Id)
                        .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                        .ToList()
                });
            return await result.ToPagedResultAsync(page, pageSize, cancellationToken);
        });
        return ApiResults.Ok(context, cached!, "Users retrieved successfully.");
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
        InvalidateAdminReads();
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
        InvalidateAdminReads();
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
        InvalidateAdminReads();

        await notifications.NotifyAsync(id, NotificationType.ProfileVerified, "Your profile is verified",
            "your profile has been verified. Verified members get priority visibility in Discovery and can send date requests.",
            cancellationToken: cancellationToken);

        return ApiResults.Ok(context, new { UserId = id, profile.IsVerified }, "Profile verified successfully.");
    }

    private static void InvalidateAdminReads() => Interlocked.Increment(ref _adminReadCacheVersion);

    private static async Task<IResult> SendInformationRequest(Guid id, SendAdminInformationRequest request,
        HttpContext context, MirageDbContext db, IEmailService email, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message) || message.Length is < 10 or > 2000)
            return EndpointHelpers.ValidationProblem(context,
                ("message", "Enter a request between 10 and 2,000 characters."));

        var recipient = await db.Users.AsNoTracking()
            .Where(x => x.Id == id && x.IsActive)
            .Select(x => new
            {
                x.Email,
                DisplayName = db.Profiles.Where(p => p.UserId == x.Id)
                    .Select(p => p.DisplayName).FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (recipient?.Email is null)
            return EndpointHelpers.NotFound(context, "An active user with an email address was not found.");

        var frontendUrl = (configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app").TrimEnd('/');
        var sent = await email.SendAdminInformationRequestEmailAsync(recipient.Email,
            recipient.DisplayName ?? "there", message, $"{frontendUrl}/profiles/{id}", cancellationToken);
        if (!sent)
            return EndpointHelpers.Problem(context, StatusCodes.Status503ServiceUnavailable,
                "Email not sent", "The information request could not be delivered. Please try again.");

        return ApiResults.Ok(context, new { UserId = id }, "Information request sent successfully.");
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
                x.Status, x.Resolution, x.ReportedByUserId, x.CreatedAt,
                TargetDisplayName = x.TargetType == ContentReportTargetType.Profile
                    ? db.Profiles.Where(profile => profile.UserId == x.TargetId)
                        .Select(profile => profile.DisplayName).FirstOrDefault()
                    : null,
                TargetAvatarUrl = x.TargetType == ContentReportTargetType.Profile
                    ? db.Profiles.Where(profile => profile.UserId == x.TargetId)
                        .Select(profile => profile.AvatarUrl).FirstOrDefault()
                    : null,
                ReporterDisplayName = db.Profiles.Where(profile => profile.UserId == x.ReportedByUserId)
                    .Select(profile => profile.DisplayName).FirstOrDefault()
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
        MirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (request.Details?.Length > 2000)
            return EndpointHelpers.ValidationProblem(context,
                ("details", "Report details cannot exceed 2,000 characters."));
        if (request.Reason == ContentReportReason.Other && string.IsNullOrWhiteSpace(request.Details))
            return EndpointHelpers.ValidationProblem(context,
                ("details", "Please describe the concern when selecting Other."));
        if (request.TargetType == ContentReportTargetType.Profile)
        {
            if (request.TargetId == userId)
                return EndpointHelpers.ValidationProblem(context, ("targetId", "You cannot report your own profile."));
            var targetExists = await db.Profiles.AsNoTracking().AnyAsync(
                profile => profile.UserId == request.TargetId &&
                           db.Users.Any(user => user.Id == profile.UserId && user.IsActive),
                cancellationToken);
            if (!targetExists) return EndpointHelpers.NotFound(context, "The reported profile was not found.");
        }

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

    private static async Task<IResult> ListOrganisations(HttpContext context, MirageDbContext db, IMemoryCache cache,
        OrganisationStatus? status, string? query, int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim().ToLowerInvariant() ?? string.Empty;
        var cacheKey =
            $"admin:organisations:v{Volatile.Read(ref _adminReadCacheVersion)}:{status}:{normalizedQuery}:{page}:{pageSize}";
        var response = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = AdminReadCacheDuration;
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

            return new Mirage.Application.Common.PagedResult<AdminOrganisationSummaryResponse>(
                paged.Items.Select(x => new AdminOrganisationSummaryResponse(x.Id, x.Name, x.Denomination, x.Country,
                    x.LogoUrl, x.WebsiteUrl, x.Status, x.AdminUserId, x.AdminDisplayName, x.AdminEmail, x.ApprovedMemberCount,
                    x.PendingMemberCount, x.BranchCount, x.ManagerCount + 1, x.CreatedAt)).ToList(),
                paged.Page, paged.PageSize, paged.TotalCount);
        });

        return ApiResults.Ok(context, response!, "Churches retrieved successfully.");
    }

    private static async Task<IResult> MergeOrganisation(Guid sourceId, MergeOrganisationRequest request,
        HttpContext context, MirageDbContext db, CancellationToken cancellationToken)
    {
        if (sourceId == request.TargetOrganisationId)
            return EndpointHelpers.ValidationProblem(context,
                ("targetOrganisationId", "Source and target organisations must be different."));

        var organisations = await db.Organisations.AsNoTracking()
            .Where(x => x.Id == sourceId || x.Id == request.TargetOrganisationId)
            .ToListAsync(cancellationToken);
        var sourceSnapshot = organisations.SingleOrDefault(x => x.Id == sourceId);
        var targetSnapshot = organisations.SingleOrDefault(x => x.Id == request.TargetOrganisationId);
        if (sourceSnapshot is null || targetSnapshot is null)
            return EndpointHelpers.NotFound(context, "The source or target organisation was not found.");

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry must not reuse entity states from the failed attempt. If the commit actually
            // succeeded but its acknowledgement was lost, the missing source makes this operation
            // an idempotent success instead of a destructive second merge.
            db.ChangeTracker.Clear();
            var currentOrganisations = await db.Organisations
                .Where(x => x.Id == sourceId || x.Id == request.TargetOrganisationId)
                .ToListAsync(cancellationToken);
            var source = currentOrganisations.SingleOrDefault(x => x.Id == sourceId);
            var target = currentOrganisations.SingleOrDefault(x => x.Id == request.TargetOrganisationId)
                ?? throw new InvalidOperationException("The canonical organisation no longer exists.");
            if (source is null) return;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Resolve uniqueness constraints before moving the remaining membership rows.
            await db.OrganisationMembers
                .Where(sourceMember => sourceMember.OrganisationId == source.Id &&
                    db.OrganisationMembers.Any(targetMember =>
                        targetMember.OrganisationId == target.Id &&
                        targetMember.UserId == sourceMember.UserId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.OrganisationManagers
                .Where(sourceManager => sourceManager.OrganisationId == source.Id &&
                    db.OrganisationManagers.Any(targetManager =>
                        targetManager.OrganisationId == target.Id &&
                        targetManager.UserId == sourceManager.UserId))
                .ExecuteDeleteAsync(cancellationToken);

            await db.OrganisationBranches.Where(x => x.OrganisationId == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id), cancellationToken);
            await db.OrganisationMembers.Where(x => x.OrganisationId == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id), cancellationToken);
            await db.OrganisationManagers.Where(x => x.OrganisationId == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id), cancellationToken);

            // Preserve the source owner as an administrator of the canonical organisation after
            // manager rows have moved, avoiding a duplicate (OrganisationId, UserId) key.
            var sourceOwnerAlreadyManagesTarget = source.AdminUserId == target.AdminUserId ||
                await db.OrganisationManagers.AnyAsync(
                    x => x.OrganisationId == target.Id && x.UserId == source.AdminUserId,
                    cancellationToken);
            if (!sourceOwnerAlreadyManagesTarget)
                db.OrganisationManagers.Add(new OrganisationManager(target.Id, source.AdminUserId, null));

            await db.OrgEvents.Where(x => x.OrganisationId == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id), cancellationToken);
            await db.Counsellors.Where(x => x.OrganisationId == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id), cancellationToken);
            await db.Recommendations.Where(x => x.OrganisationId == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id), cancellationToken);
            await db.CounsellorInvites.Where(x => x.OrganisationId == source.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id), cancellationToken);

            // Organisation-backed communities carry user-generated posts. When both organisations
            // have the same category, combine their memberships and posts before removing the shell.
            var sourceCommunities = await db.Communities
                .Where(x => x.OrganisationId == source.Id)
                .ToListAsync(cancellationToken);
            var targetCommunities = await db.Communities
                .Where(x => x.OrganisationId == target.Id)
                .ToListAsync(cancellationToken);
            foreach (var sourceCommunity in sourceCommunities)
            {
                var targetCommunity = targetCommunities.FirstOrDefault(x =>
                    x.Category.Equals(sourceCommunity.Category, StringComparison.OrdinalIgnoreCase));
                if (targetCommunity is null)
                {
                    await db.Communities.Where(x => x.Id == sourceCommunity.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OrganisationId, target.Id),
                            cancellationToken);
                    continue;
                }

                await db.CommunityMembers
                    .Where(sourceMember => sourceMember.CommunityId == sourceCommunity.Id &&
                        db.CommunityMembers.Any(targetMember =>
                            targetMember.CommunityId == targetCommunity.Id &&
                            targetMember.UserId == sourceMember.UserId))
                    .ExecuteDeleteAsync(cancellationToken);
                await db.CommunityMembers.Where(x => x.CommunityId == sourceCommunity.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CommunityId, targetCommunity.Id),
                        cancellationToken);
                await db.CommunityPosts.Where(x => x.CommunityId == sourceCommunity.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.CommunityId, targetCommunity.Id),
                        cancellationToken);
                db.Communities.Remove(sourceCommunity);
            }

            db.Organisations.Remove(source);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        InvalidateAdminReads();

        return ApiResults.Ok(context,
            new { SourceOrganisationId = sourceSnapshot.Id, TargetOrganisationId = targetSnapshot.Id },
            $"“{sourceSnapshot.Name}” was merged into “{targetSnapshot.Name}”. Members, administrators, branches, events, counsellors, and community content were preserved.");
    }

    // Reconciles the database against the curated starter directory (SeedData/nigerian-churches.json)
    // via the shared ChurchDirectorySync — same reconciliation the automatic startup sweep runs on
    // every deploy (DatabaseInitialiser.SyncChurchDirectoryAsync), except this one has a PlatformAdmin
    // context and so can also create brand-new churches (owned by whoever clicks the button; real
    // church admins are added afterwards via the existing invite flow, since ownership can't be
    // transferred once set). Useful to re-run on demand without waiting for the next deploy.
    private static async Task<IResult> SeedChurches(HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var actorId = context.User.GetUserId();
        var path = Path.Combine(AppContext.BaseDirectory, "SeedData", "nigerian-churches.json");
        if (!File.Exists(path))
            return EndpointHelpers.Problem(context, StatusCodes.Status500InternalServerError,
                "Seed data missing", "The church seed data file was not found on this deployment.");

        var result = await ChurchDirectorySync.SyncAsync(db, path, actorId, cancellationToken);
        InvalidateAdminReads();
        return ApiResults.Ok(context,
            new
            {
                result.Created, result.Skipped, result.BranchesCreated, result.DenominationsUpdated, result.Retired
            },
            $"Seeded {result.Created} church(es) with {result.BranchesCreated} branch(es); " +
            $"updated {result.DenominationsUpdated} denomination(s); retired {result.Retired}; " +
            $"skipped {result.Skipped} already present.");
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
