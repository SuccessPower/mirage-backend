using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Hubs;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;
// ReSharper disable once RedundantUsingDirective

namespace Mirage.Api.Endpoints;

internal static class CounsellingEndpoints
{
    public static RouteGroupBuilder MapCounsellingEndpoints(this RouteGroupBuilder api)
    {
        var counsellors = api.MapGroup("/counsellors").WithTags("Counselling");
        counsellors.MapGet("/", ListCounsellors);
        counsellors.MapGet("/{id:guid}", GetCounsellor);
        counsellors.MapGet("/me", GetMyCounsellorProfile).RequireAuthorization(MiragePolicy.Counsellor);
        counsellors.MapPut("/me", UpdateMyCounsellorProfile).RequireAuthorization(MiragePolicy.Counsellor);
        counsellors.MapPut("/me/verification-documents", UpdateVerificationDocuments).RequireAuthorization(MiragePolicy.Counsellor);
        counsellors.MapPost("/apply", Apply).RequireAuthorization();
        counsellors.MapPost("/me/charging-request", RequestCharging).RequireAuthorization(MiragePolicy.Counsellor);
        counsellors.MapGet("/me/ratings", GetMyRatings).RequireAuthorization(MiragePolicy.Counsellor);
        counsellors.MapGet("/me/banks", ListBanks).RequireAuthorization(MiragePolicy.Counsellor);
        counsellors.MapPost("/me/resolve-bank-account", ResolveBankAccount).RequireAuthorization(MiragePolicy.Counsellor);
        counsellors.MapPost("/me/bank-account", SaveBankAccount).RequireAuthorization(MiragePolicy.Counsellor);

        var sessions = api.MapGroup("/sessions").WithTags("Counselling").RequireAuthorization();
        sessions.MapGet("/", ListSessions);
        sessions.MapGet("/{id:guid}", GetSession);
        sessions.MapPost("/", Book);
        sessions.MapPatch("/{id:guid}/accept", AcceptSession);
        sessions.MapPatch("/{id:guid}/decline", DeclineSession);
        sessions.MapPatch("/{id:guid}/start", StartSession);
        sessions.MapPatch("/{id:guid}/complete", CompleteSession);
        sessions.MapPatch("/{id:guid}/cancel", CancelSession);
        sessions.MapPatch("/{id:guid}/partner-accept", AcceptPartnerInvite);
        sessions.MapPost("/{id:guid}/trust-unlock", ConsentToTrustUnlock);
        sessions.MapPost("/{id:guid}/notes", AddNote);
        sessions.MapGet("/{id:guid}/notes", GetNotes);
        sessions.MapPost("/{id:guid}/rating", RateSession);

        // Private channel: messages + follow-up meetings between counsellor and client
        sessions.MapGet("/{id:guid}/messages", ListMessages);
        sessions.MapPost("/{id:guid}/messages", SendMessage);
        sessions.MapGet("/{id:guid}/meetings", ListMeetings);
        sessions.MapPost("/{id:guid}/meetings", ScheduleMeeting);
        sessions.MapGet("/{id:guid}/video-token", GetVideoToken);
        sessions.MapGet("/{id:guid}/meetings/{meetingId:guid}/video-token", GetMeetingVideoToken);
        return api;
    }

    private static async Task<bool> IsSessionPartyAsync(Guid sessionId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        await db.CounsellingSessions.AsNoTracking().AnyAsync(x => x.Id == sessionId
            && (x.ClientUserId == userId || x.Counsellor.UserId == userId
                || (x.PartnerUserId == userId && x.PartnerAccepted))
            && x.Status != SessionStatus.Declined && x.Status != SessionStatus.Cancelled, cancellationToken);

    private static async Task<IResult> ListMessages(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsSessionPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var messages = await db.CounsellingMessages.AsNoTracking()
            .Where(x => x.SessionId == id)
            .OrderBy(x => x.CreatedAt)
            .Join(db.Profiles.AsNoTracking(), m => m.SenderId, p => p.UserId, (m, p) => new CounsellingMessageResponse(
                m.Id, m.SessionId, m.SenderId, p.DisplayName, m.Content, m.Type, m.AttachmentUrl, m.CreatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, messages, "Messages retrieved successfully.");
    }

    private static async Task<IResult> SendMessage(Guid id, SendCounsellingMessageRequest request, HttpContext context,
        IMirageDbContext db, IHubContext<ChatHub> hub, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsSessionPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Message content is required."));

        var message = new CounsellingMessage(id, userId, request.Content, request.Type, request.AttachmentUrl);
        db.CounsellingMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await hub.Clients.Group($"counsellingsession:{id}").SendAsync("ReceiveCounsellingMessage", new
        {
            message.Id,
            SessionId = id,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        }, cancellationToken);

        return ApiResults.Created(context, $"/api/v1/sessions/{id}/messages/{message.Id}", new { message.Id },
            "Message sent successfully.");
    }

    private static async Task<IResult> ListMeetings(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsSessionPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var meetings = await db.CounsellingMeetings.AsNoTracking()
            .Where(x => x.SessionId == id)
            .OrderBy(x => x.ScheduledAt)
            .Select(x => new CounsellingMeetingResponse(x.Id, x.SessionId, x.ScheduledByUserId, x.Title,
                x.MeetingLink, x.ScheduledAt, x.DurationMinutes))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, meetings, "Meetings retrieved successfully.");
    }

    private static async Task<IResult> ScheduleMeeting(Guid id, ScheduleCounsellingMeetingRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsSessionPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Title))
            return EndpointHelpers.ValidationProblem(context, ("meeting", "Title is required."));

        var meeting = new CounsellingMeeting(id, userId, request.Title, request.ScheduledAt,
            request.DurationMinutes);
        db.CounsellingMeetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);

        var session = await db.CounsellingSessions.AsNoTracking().Include(x => x.Counsellor)
            .SingleAsync(x => x.Id == id, cancellationToken);
        var otherUserId = session.ClientUserId == userId ? session.Counsellor.UserId : session.ClientUserId;
        await notifications.NotifyAsync(otherUserId, NotificationType.SessionBooked, "New meeting scheduled",
            $"{request.Title} was scheduled for {request.ScheduledAt:MMM d, h:mm tt}.", meeting.Id, "CounsellingMeeting",
            cancellationToken);

        return ApiResults.Created(context, $"/api/v1/sessions/{id}/meetings/{meeting.Id}", new { meeting.Id },
            "Meeting scheduled successfully.");
    }

    private static async Task<IResult> GetVideoToken(Guid id, HttpContext context, MirageDbContext db,
        JitsiService jitsi, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsSessionPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var session = await db.CounsellingSessions.AsNoTracking().Include(x => x.Counsellor)
            .SingleAsync(x => x.Id == id, cancellationToken);
        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "Guest";
        var email = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId).Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);

        var room = $"mirage-session-{id:N}";
        var isModerator = session.Counsellor.UserId == userId;
        var token = jitsi.CreateToken(userId, displayName, email, room, isModerator);

        return ApiResults.Ok(context, new { AppId = jitsi.AppId, Room = room, Token = token },
            "Video token issued successfully.");
    }

    private static async Task<IResult> GetMeetingVideoToken(Guid id, Guid meetingId, HttpContext context,
        MirageDbContext db, JitsiService jitsi, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsSessionPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var meeting = await db.CounsellingMeetings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == meetingId && x.SessionId == id, cancellationToken);
        if (meeting is null) return EndpointHelpers.NotFound(context, "Meeting was not found.");

        var session = await db.CounsellingSessions.AsNoTracking().Include(x => x.Counsellor)
            .SingleAsync(x => x.Id == id, cancellationToken);
        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "Guest";
        var email = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId).Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);

        var isModerator = session.Counsellor.UserId == userId;
        var token = jitsi.CreateToken(userId, displayName, email, meeting.MeetingLink, isModerator);

        return ApiResults.Ok(context, new { AppId = jitsi.AppId, Room = meeting.MeetingLink, Token = token },
            "Video token issued successfully.");
    }

    private static async Task<IResult> ListCounsellors(HttpContext context, IMirageDbContext db,
        string? specialisation,
        string? language, bool freeOnly = false, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = context.User.TryGetUserId();
        var query = db.Counsellors.AsNoTracking().Where(x => x.IsApproved);
        if (currentUserId is not null) query = query.Where(x => x.UserId != currentUserId);
        if (freeOnly) query = query.Where(x => x.AcceptsFreeSessions);
        if (!string.IsNullOrWhiteSpace(specialisation))
            query = query.Where(x => x.Specialisations.Any(value => EF.Functions.ILike(value, $"%{specialisation}%")));
        if (!string.IsNullOrWhiteSpace(language))
            query = query.Where(x => x.Languages.Any(value => EF.Functions.ILike(value, $"%{language}%")));
        var result = query.OrderByDescending(x => x.YearsExperience).Select(x => new
        {
            x.Id,
            x.UserProfile.DisplayName,
            x.UserProfile.Denomination,
            x.UserProfile.AvatarUrl,
            Organisation = x.Organisation != null ? x.Organisation.Name : null,
            x.YearsExperience,
            IsAnonymous = false,
            x.AcceptsFreeSessions,
            x.Specialisations,
            x.Languages,
            x.PriceAmount,
            x.PriceCurrency,
            x.SupportsVoiceCalls,
            x.SupportsVideoCalls,
            x.AverageRating,
            x.RatingCount
        });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Counsellors retrieved successfully.");
    }

    private static async Task<IResult> ListSessions(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var sessions = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.ClientUserId == userId || x.Counsellor.UserId == userId || x.PartnerUserId == userId)
            .OrderByDescending(x => x.ScheduledAt)
            .Select(x => new CounsellingSessionResponse(
                x.Id,
                x.CounsellorId,
                x.Counsellor.UserId,
                x.Counsellor.UserProfile.DisplayName,
                x.Counsellor.UserProfile.AvatarUrl,
                x.ClientUserId,
                x.ClientAnonymous && userId == x.Counsellor.UserId
                    ? "Anonymous client"
                    : db.Profiles.Where(profile => profile.UserId == x.ClientUserId)
                        .Select(profile => profile.DisplayName)
                        .SingleOrDefault() ?? "Client",
                x.ClientAnonymous && userId == x.Counsellor.UserId
                    ? null
                    : db.Profiles.Where(profile => profile.UserId == x.ClientUserId)
                        .Select(profile => profile.AvatarUrl)
                        .SingleOrDefault(),
                x.Type,
                x.ScheduledAt,
                x.Status,
                x.Topic,
                x.ClientAnonymous,
                x.TrustUnlockStatus,
                x.PartnerUserId,
                x.PartnerAccepted,
                x.CreatedAt,
                x.UpdatedAt,
                x.Status == SessionStatus.Scheduled || x.Status == SessionStatus.InProgress || x.Status == SessionStatus.Completed
                    ? x.Counsellor.PhoneNumber
                    : null,
                x.Payment != null ? x.Payment.Id : (Guid?)null,
                db.SessionRatings.Any(r => r.SessionId == x.Id)))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, sessions, "Counselling sessions retrieved successfully.");
    }

    // Runs regardless of whether the session required payment first — a couple's partner must be
    // able to see the session and join its chat as soon as it's booked, not only once payment
    // clears (paid Couples sessions used to never invite the partner because this only ran on the
    // free-session path).
    private static async Task InvitePartnerIfApplicableAsync(CounsellingSession session, SessionType type,
        string? partnerEmail, MirageDbContext db, NotificationService notifications,
        CancellationToken cancellationToken)
    {
        if (type != SessionType.Couples || string.IsNullOrWhiteSpace(partnerEmail)) return;

        var partnerUserId = await db.Users.AsNoTracking()
            .Where(x => x.Email != null && x.Email.ToLower() == partnerEmail.Trim().ToLower())
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!partnerUserId.HasValue) return;

        session.InvitePartner(partnerUserId.Value);
        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyAsync(partnerUserId.Value, NotificationType.SessionBooked,
            "Couples counselling invitation",
            "Your partner invited you to a couples counselling session.",
            session.Id, "CounsellingSession", cancellationToken);
    }

    private static async Task FinalizeBookedSessionAsync(CounsellingSession session, BookSessionRequest request,
        MirageDbContext db, NotificationService notifications, Guid clientUserId, Guid counsellorUserId,
        CancellationToken cancellationToken)
    {
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(session.Id, clientUserId,
            $"Session requested; clientAnonymous={request.ClientAnonymous}; counsellorAnonymous=false"));
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(counsellorUserId, NotificationType.SessionBooked,
            "New session request", $"A new {request.Type.ToString().ToLowerInvariant()} session was requested.",
            session.Id, "CounsellingSession", cancellationToken);
    }

    private static async Task<IResult> Book(BookSessionRequest request, HttpContext context,
        MirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        if (request.ScheduledAt <= DateTimeOffset.UtcNow)
            return EndpointHelpers.ValidationProblem(context,
                ("scheduledAt", "Session must be scheduled in the future."));
        var counsellor = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == request.CounsellorId && x.IsApproved)
            .Select(x => new { x.UserId, x.AcceptsFreeSessions, x.PriceAmount, x.PriceCurrency })
            .SingleOrDefaultAsync(cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Approved counsellor was not found.");

        var requiresPayment = !counsellor.AcceptsFreeSessions;
        if (requiresPayment && (counsellor.PriceAmount is null || string.IsNullOrWhiteSpace(counsellor.PriceCurrency)))
            return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Pricing not configured", "This counsellor has not set a session price yet.");

        var userId = context.User.GetUserId();
        if (!await db.Profiles.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsVerified, cancellationToken))
            return EndpointHelpers.Forbidden(context, "Verify your profile before booking a counselling session.");

        var session = new CounsellingSession(request.CounsellorId, userId, request.Type,
            request.ScheduledAt, request.Topic, false, request.ClientAnonymous);
        db.CounsellingSessions.Add(session);

        Payment? payment = null;
        if (requiresPayment)
        {
            session.MarkAwaitingPayment();
            payment = new Payment(session.Id, userId, request.CounsellorId, counsellor.PriceAmount!.Value,
                counsellor.PriceCurrency!);
            db.Payments.Add(payment);
        }

        await db.SaveChangesAsync(cancellationToken);

        await InvitePartnerIfApplicableAsync(session, request.Type, request.PartnerEmail, db, notifications,
            cancellationToken);

        if (!requiresPayment)
            await FinalizeBookedSessionAsync(session, request, db, notifications, userId, counsellor.UserId,
                cancellationToken);

        var response = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.Id == session.Id)
            .Select(x => new CounsellingSessionResponse(
                x.Id,
                x.CounsellorId,
                x.Counsellor.UserId,
                x.Counsellor.UserProfile.DisplayName,
                x.Counsellor.UserProfile.AvatarUrl,
                x.ClientUserId,
                db.Profiles.Where(profile => profile.UserId == x.ClientUserId)
                    .Select(profile => profile.DisplayName)
                    .SingleOrDefault() ?? "Client",
                db.Profiles.Where(profile => profile.UserId == x.ClientUserId)
                    .Select(profile => profile.AvatarUrl)
                    .SingleOrDefault(),
                x.Type,
                x.ScheduledAt,
                x.Status,
                x.Topic,
                x.ClientAnonymous,
                x.TrustUnlockStatus,
                x.PartnerUserId,
                x.PartnerAccepted,
                x.CreatedAt,
                x.UpdatedAt,
                x.Status == SessionStatus.Scheduled || x.Status == SessionStatus.InProgress || x.Status == SessionStatus.Completed
                    ? x.Counsellor.PhoneNumber
                    : null,
                x.Payment != null ? x.Payment.Id : (Guid?)null,
                false))
            .SingleAsync(cancellationToken);

        return ApiResults.Created(context, $"/api/v1/sessions/{session.Id}",
            new { Session = response, RequiresPayment = requiresPayment, PaymentId = payment?.Id },
            requiresPayment
                ? "Payment is required to confirm this session."
                : "Counselling session requested successfully.");
    }

    private static async Task<IResult> ConsentToTrustUnlock(Guid id, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Counselling session was not found.");
        var isClient = session.ClientUserId == userId;
        if (!isClient && session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        session.ConsentToReveal(isClient);
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(id, userId, "ConsentedToTrustUnlock"));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { session.TrustUnlockStatus },
            "Trust unlock consent recorded successfully.");
    }

    private static async Task<IResult> GetCounsellor(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var currentUserId = context.User.TryGetUserId();
        var isAcceptedClient = currentUserId is not null && await db.CounsellingSessions.AsNoTracking()
            .AnyAsync(x => x.CounsellorId == id && x.ClientUserId == currentUserId
                && x.Status != SessionStatus.Declined && x.Status != SessionStatus.Cancelled, cancellationToken);

        var counsellor = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == id && x.IsApproved)
            .Select(x => new
            {
                x.Id,
                x.UserProfile.DisplayName,
                x.UserProfile.Denomination,
                x.UserProfile.AvatarUrl,
                Organisation = x.Organisation != null ? x.Organisation.Name : null,
                x.YearsExperience,
                IsAnonymous = false,
                x.AcceptsFreeSessions,
                x.Specialisations,
                x.Languages,
                x.PriceAmount,
                x.PriceCurrency,
                x.SupportsVoiceCalls,
                x.SupportsVideoCalls,
                x.AverageRating,
                x.RatingCount,
                PhoneNumber = isAcceptedClient ? x.PhoneNumber : null
            })
            .SingleOrDefaultAsync(cancellationToken);
        return counsellor is null
            ? EndpointHelpers.NotFound(context, "Counsellor was not found.")
            : ApiResults.Ok(context, counsellor, "Counsellor retrieved successfully.");
    }

    private static async Task<IResult> GetMyCounsellorProfile(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Counsellors.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.OrganisationId,
                Organisation = x.Organisation != null ? x.Organisation.Name : null,
                x.YearsExperience,
                x.IsApproved,
                x.IsAnonymous,
                x.AcceptsFreeSessions,
                x.CompletedFreeSessionsCount,
                IsEligibleToCharge = x.CompletedFreeSessionsCount >= CounsellorProfile.MinimumFreeSessionsBeforeCharging,
                x.Specialisations,
                x.Languages,
                x.PhoneNumber,
                x.PriceAmount,
                x.PriceCurrency,
                x.SupportsVoiceCalls,
                x.SupportsVideoCalls,
                x.AverageRating,
                x.RatingCount,
                x.ChargingRequested,
                x.BankName,
                x.BankAccountNumber,
                x.BankAccountName,
                HasPayoutAccount = x.PaystackSubaccountCode != null || x.FlutterwaveSubaccountId != null
            })
            .SingleOrDefaultAsync(cancellationToken);
        return profile is null
            ? EndpointHelpers.NotFound(context, "Counsellor profile was not found.")
            : ApiResults.Ok(context, profile, "Counsellor profile retrieved successfully.");
    }

    private static async Task<IResult> RequestCharging(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Counsellors.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Counsellor profile was not found.");
        try { profile.RequestCharging(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.Id, profile.ChargingRequested },
            "Charging request submitted — an admin will review it shortly.");
    }

    private static async Task<IResult> ListBanks(HttpContext context, PaystackService paystack,
        CancellationToken cancellationToken)
    {
        var banks = await paystack.ListBanksAsync(cancellationToken);
        return ApiResults.Ok(context, banks, "Banks retrieved successfully.");
    }

    private static async Task<IResult> ResolveBankAccount(ResolveBankAccountRequest request, HttpContext context,
        PaystackService paystack, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BankCode) || string.IsNullOrWhiteSpace(request.AccountNumber))
            return EndpointHelpers.ValidationProblem(context, ("accountNumber", "Bank and account number are required."));
        var resolved = await paystack.ResolveAccountAsync(request.BankCode, request.AccountNumber, cancellationToken);
        return resolved is null
            ? EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Could not resolve account", "Check the bank and account number and try again.")
            : ApiResults.Ok(context, resolved, "Account resolved successfully.");
    }

    // Creates a payout subaccount on both providers from one bank entry, so the counsellor
    // only has to do this once regardless of which provider a future client pays through.
    // Best-effort: a failure on one provider doesn't block the other from succeeding.
    private static async Task<IResult> SaveBankAccount(SaveBankAccountRequest request, HttpContext context,
        IMirageDbContext db, PaystackService paystack, FlutterwaveService flutterwave,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BankCode) || string.IsNullOrWhiteSpace(request.AccountNumber)
            || string.IsNullOrWhiteSpace(request.AccountName))
            return EndpointHelpers.ValidationProblem(context, ("accountNumber", "Bank, account number, and account name are required."));

        var userId = context.User.GetUserId();
        var profile = await db.Counsellors.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Counsellor profile was not found.");

        profile.SetBankAccount(request.BankCode, request.BankName, request.AccountNumber, request.AccountName);

        var platformCommissionPercentage = Payment.PlatformCommissionRate * 100;
        var counsellorSplitFraction = 1 - Payment.PlatformCommissionRate;
        var errors = new List<string>();

        try
        {
            var subaccountCode = await paystack.CreateSubaccountAsync(request.AccountName, request.BankCode,
                request.AccountNumber, platformCommissionPercentage, cancellationToken);
            profile.SetPaystackSubaccountCode(subaccountCode);
        }
        catch (Exception) { errors.Add("Paystack"); }

        try
        {
            var subaccountId = await flutterwave.CreateSubaccountAsync(request.AccountName, request.BankCode,
                request.AccountNumber, counsellorSplitFraction, cancellationToken);
            profile.SetFlutterwaveSubaccountId(subaccountId);
        }
        catch (Exception) { errors.Add("Flutterwave"); }

        await db.SaveChangesAsync(cancellationToken);

        if (errors.Count == 2)
            return EndpointHelpers.Problem(context, StatusCodes.Status502BadGateway,
                "Payout setup failed", "Could not set up payouts with either provider. Please try again.");

        return ApiResults.Ok(context,
            new { profile.Id, profile.PaystackSubaccountCode, profile.FlutterwaveSubaccountId },
            errors.Count == 0
                ? "Payout account saved successfully."
                : $"Payout account saved, but setup with {errors[0]} failed — payments through that provider won't split until this is retried.");
    }

    private static async Task<IResult> GetMyRatings(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var counsellor = await db.Counsellors.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Counsellor profile was not found.");

        var ratings = await db.SessionRatings.AsNoTracking()
            .Join(db.CounsellingSessions.AsNoTracking(), r => r.SessionId, s => s.Id, (r, s) => new { r, s })
            .Where(x => x.s.CounsellorId == counsellor.Id)
            .OrderByDescending(x => x.r.CreatedAt)
            .Select(x => new
            {
                x.r.Id,
                x.r.SessionId,
                x.s.Topic,
                x.r.Rating,
                x.r.Comment,
                x.r.CreatedAt,
                ClientDisplayName = x.s.ClientAnonymous
                    ? "Anonymous client"
                    : db.Profiles.Where(p => p.UserId == x.s.ClientUserId).Select(p => p.DisplayName).SingleOrDefault() ?? "Client"
            })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, ratings, "Ratings retrieved successfully.");
    }

    private static async Task<IResult> Apply(ApplyCounsellorRequest request, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        if (request.YearsExperience < 0)
            return EndpointHelpers.ValidationProblem(context, ("yearsExperience", "Years of experience must be 0 or greater."));

        var userId = context.User.GetUserId();
        if (await db.Counsellors.AnyAsync(x => x.UserId == userId, cancellationToken))
            return EndpointHelpers.Conflict(context, "You already have a counsellor application on file.");

        Organisation? org = null;
        if (request.OrganisationId is { } organisationId)
        {
            org = await db.Organisations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organisationId, cancellationToken);
            if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        }

        var profile = new CounsellorProfile(userId, request.OrganisationId, request.YearsExperience,
            request.Specialisations, request.Languages, request.VerificationDocumentUrls);
        db.Counsellors.Add(profile);
        await db.SaveChangesAsync(cancellationToken);

        if (org is not null)
        {
            var displayName = await db.Profiles.AsNoTracking()
                .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
            await notifications.NotifyAsync(org.AdminUserId, NotificationType.CounsellorApplicationReceived,
                "New counsellor application", $"{displayName ?? "A member"} applied to become a counsellor for {org.Name}.",
                profile.Id, "Counsellor", cancellationToken);
        }

        return ApiResults.Created(context, $"/api/v1/counsellors/{profile.Id}", new { profile.Id },
            org is not null
                ? "Application submitted! Your organisation admin will review it."
                : "Application submitted! A super admin will review your documents before you appear publicly.");
    }

    private static async Task<IResult> UpdateMyCounsellorProfile(UpdateCounsellorProfileRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.YearsExperience < 0)
            return EndpointHelpers.ValidationProblem(context, ("yearsExperience", "Years of experience must be 0 or greater."));
        var userId = context.User.GetUserId();
        var profile = await db.Counsellors.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Counsellor profile was not found.");
        try { profile.UpdateProfile(request.YearsExperience, request.Specialisations, request.Languages, request.AcceptsFreeSessions); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        profile.ToggleAnonymity(false);
        profile.SetContactAndPricing(request.PhoneNumber, request.PriceAmount, request.PriceCurrency,
            request.SupportsVoiceCalls, request.SupportsVideoCalls);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.Id }, "Counsellor profile updated successfully.");
    }

    private static async Task<IResult> UpdateVerificationDocuments(UpdateVerificationDocumentsRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.DocumentUrls is null || request.DocumentUrls.Length == 0)
            return EndpointHelpers.ValidationProblem(context, ("documentUrls", "At least one document is required."));
        var userId = context.User.GetUserId();
        var profile = await db.Counsellors.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Counsellor profile was not found.");
        profile.SetVerificationDocuments(request.DocumentUrls);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.Id, profile.VerificationDocumentUrls },
            "Verification documents submitted successfully.");
    }

    private static async Task<IResult> GetSession(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.Id == id && (x.ClientUserId == userId || x.Counsellor.UserId == userId
                || x.PartnerUserId == userId))
            .Select(x => new CounsellingSessionResponse(
                x.Id,
                x.CounsellorId,
                x.Counsellor.UserId,
                x.Counsellor.UserProfile.DisplayName,
                x.Counsellor.UserProfile.AvatarUrl,
                x.ClientUserId,
                x.ClientAnonymous && userId == x.Counsellor.UserId
                    ? "Anonymous client"
                    : db.Profiles.Where(profile => profile.UserId == x.ClientUserId)
                        .Select(profile => profile.DisplayName)
                        .SingleOrDefault() ?? "Client",
                x.ClientAnonymous && userId == x.Counsellor.UserId
                    ? null
                    : db.Profiles.Where(profile => profile.UserId == x.ClientUserId)
                        .Select(profile => profile.AvatarUrl)
                        .SingleOrDefault(),
                x.Type,
                x.ScheduledAt,
                x.Status,
                x.Topic,
                x.ClientAnonymous,
                x.TrustUnlockStatus,
                x.PartnerUserId,
                x.PartnerAccepted,
                x.CreatedAt,
                x.UpdatedAt,
                x.Status == SessionStatus.Scheduled || x.Status == SessionStatus.InProgress || x.Status == SessionStatus.Completed
                    ? x.Counsellor.PhoneNumber
                    : null,
                x.Payment != null ? x.Payment.Id : (Guid?)null,
                db.SessionRatings.Any(r => r.SessionId == x.Id)))
            .SingleOrDefaultAsync(cancellationToken);
        return session is null
            ? EndpointHelpers.NotFound(context, "Session was not found.")
            : ApiResults.Ok(context, session, "Session retrieved successfully.");
    }

    private static async Task<IResult> AcceptSession(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        try { session.Accept(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(id, userId, "SessionAccepted"));
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(session.ClientUserId, NotificationType.SessionAccepted,
            "Session accepted", "Your counselling session request was accepted.",
            session.Id, "CounsellingSession", cancellationToken);

        return ApiResults.Ok(context, new { session.Id, session.Status }, "Session accepted.");
    }

    private static async Task<IResult> DeclineSession(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        try { session.Decline(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(session.ClientUserId, NotificationType.SessionDeclined,
            "Session declined", "Your counselling session request was declined.",
            session.Id, "CounsellingSession", cancellationToken);

        return ApiResults.Ok(context, new { session.Id, session.Status }, "Session declined.");
    }

    private static async Task<IResult> StartSession(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        try { session.Start(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(id, userId, "SessionStarted"));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { session.Id, session.Status }, "Session started.");
    }

    private static async Task<IResult> CompleteSession(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        try { session.Complete(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        session.Counsellor.RecordCompletedFreeSession();
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(id, userId, "SessionCompleted"));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { session.Id, session.Status }, "Session completed.");
    }

    private static async Task<IResult> CancelSession(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor).Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.ClientUserId != userId && session.Counsellor.UserId != userId)
            return EndpointHelpers.Forbidden(context);
        try { session.Cancel(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        session.Payment?.MarkFailed();
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(id, userId, "SessionCancelled"));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { session.Id, session.Status }, "Session cancelled.");
    }

    private static async Task<IResult> AcceptPartnerInvite(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.PartnerUserId != userId) return EndpointHelpers.Forbidden(context);
        try { session.AcceptPartnerInvite(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { session.Id, session.PartnerAccepted }, "Partner invite accepted.");
    }

    private static async Task<IResult> AddNote(Guid id, AddSessionNoteRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Note content is required."));
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        var note = new SessionNote(id, userId, request.Content);
        db.SessionNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/sessions/{id}/notes", new { note.Id }, "Session note added.");
    }

    private static async Task<IResult> GetNotes(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.AsNoTracking().Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        var notes = await db.SessionNotes.AsNoTracking()
            .Where(x => x.SessionId == id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Content, x.CreatedAt, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, notes, "Session notes retrieved successfully.");
    }

    private static async Task<IResult> RateSession(Guid id, RateSessionRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.Rating is < 1 or > 5)
            return EndpointHelpers.ValidationProblem(context, ("rating", "Rating must be between 1 and 5."));
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.ClientUserId == userId, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.Status != SessionStatus.Completed)
            return EndpointHelpers.Conflict(context, "Only completed sessions can be rated.");
        if (await db.SessionRatings.AnyAsync(x => x.SessionId == id && x.ReviewerUserId == userId, cancellationToken))
            return EndpointHelpers.Conflict(context, "Session has already been rated.");
        var rating = new SessionRating(id, userId, request.Rating, request.Comment);
        db.SessionRatings.Add(rating);

        var counsellor = await db.Counsellors.SingleOrDefaultAsync(x => x.Id == session.CounsellorId, cancellationToken);
        counsellor?.RecordRating(request.Rating);

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/sessions/{id}/rating", new { rating.Id }, "Session rated successfully.");
    }
}
