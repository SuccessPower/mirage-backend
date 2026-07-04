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

        var sessions = api.MapGroup("/sessions").WithTags("Counselling").RequireAuthorization();
        sessions.MapGet("/", ListSessions);
        sessions.MapGet("/{id:guid}", GetSession);
        sessions.MapPost("/", Book);
        sessions.MapPatch("/{id:guid}/accept", AcceptSession);
        sessions.MapPatch("/{id:guid}/decline", DeclineSession);
        sessions.MapPatch("/{id:guid}/start", StartSession);
        sessions.MapPatch("/{id:guid}/complete", CompleteSession);
        sessions.MapPatch("/{id:guid}/cancel", CancelSession);
        sessions.MapPost("/{id:guid}/trust-unlock", ConsentToTrustUnlock);
        sessions.MapPost("/{id:guid}/notes", AddNote);
        sessions.MapGet("/{id:guid}/notes", GetNotes);
        sessions.MapPost("/{id:guid}/rating", RateSession);

        // Private channel: messages + follow-up meetings between counsellor and client
        sessions.MapGet("/{id:guid}/messages", ListMessages);
        sessions.MapPost("/{id:guid}/messages", SendMessage);
        sessions.MapGet("/{id:guid}/meetings", ListMeetings);
        sessions.MapPost("/{id:guid}/meetings", ScheduleMeeting);
        return api;
    }

    private static async Task<bool> IsSessionPartyAsync(Guid sessionId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        await db.CounsellingSessions.AsNoTracking().AnyAsync(x => x.Id == sessionId
            && (x.ClientUserId == userId || x.Counsellor.UserId == userId)
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
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.MeetingLink))
            return EndpointHelpers.ValidationProblem(context, ("meeting", "Title and meeting link are required."));

        var meeting = new CounsellingMeeting(id, userId, request.Title, request.MeetingLink, request.ScheduledAt,
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

    private static async Task<IResult> ListCounsellors(HttpContext context, IMirageDbContext db,
        string? specialisation,
        string? language, bool freeOnly = false, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = db.Counsellors.AsNoTracking().Where(x => x.IsApproved);
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
            Organisation = x.Organisation != null ? x.Organisation.Name : null,
            x.YearsExperience,
            IsAnonymous = false,
            x.AcceptsFreeSessions,
            x.Specialisations,
            x.Languages
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
            .Where(x => x.ClientUserId == userId || x.Counsellor.UserId == userId)
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
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, sessions, "Counselling sessions retrieved successfully.");
    }

    private static async Task<IResult> Book(BookSessionRequest request, HttpContext context,
        MirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        if (request.ScheduledAt <= DateTimeOffset.UtcNow)
            return EndpointHelpers.ValidationProblem(context,
                ("scheduledAt", "Session must be scheduled in the future."));
        var counsellor = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == request.CounsellorId && x.IsApproved)
            .Select(x => new { x.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Approved counsellor was not found.");
        var userId = context.User.GetUserId();
        var session = new CounsellingSession(request.CounsellorId, userId, request.Type,
            request.ScheduledAt, request.Topic, false, request.ClientAnonymous);
        db.CounsellingSessions.Add(session);
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(session.Id, userId,
            $"Session requested; clientAnonymous={request.ClientAnonymous}; counsellorAnonymous=false"));
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(counsellor.UserId, NotificationType.SessionBooked,
            "New session request", $"A new {request.Type.ToString().ToLowerInvariant()} session was requested.",
            session.Id, "CounsellingSession", cancellationToken);

        if (request.Type == SessionType.Couples && !string.IsNullOrWhiteSpace(request.PartnerEmail))
        {
            var partnerUserId = await db.Users.AsNoTracking()
                .Where(x => x.Email != null && x.Email.ToLower() == request.PartnerEmail.Trim().ToLower())
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (partnerUserId.HasValue)
                await notifications.NotifyAsync(partnerUserId.Value, NotificationType.SessionBooked,
                    "Couples counselling invitation",
                    "Your partner invited you to a couples counselling session.",
                    session.Id, "CounsellingSession", cancellationToken);
        }

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
                x.CreatedAt,
                x.UpdatedAt))
            .SingleAsync(cancellationToken);

        return ApiResults.Created(context, $"/api/v1/sessions/{session.Id}", response,
            "Counselling session requested successfully.");
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
        var counsellor = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == id && x.IsApproved)
            .Select(x => new
            {
                x.Id,
                x.UserProfile.DisplayName,
                x.UserProfile.Denomination,
                Organisation = x.Organisation != null ? x.Organisation.Name : null,
                x.YearsExperience,
                IsAnonymous = false,
                x.AcceptsFreeSessions,
                x.Specialisations,
                x.Languages
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
                x.Languages
            })
            .SingleOrDefaultAsync(cancellationToken);
        return profile is null
            ? EndpointHelpers.NotFound(context, "Counsellor profile was not found.")
            : ApiResults.Ok(context, profile, "Counsellor profile retrieved successfully.");
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
            .Where(x => x.Id == id && (x.ClientUserId == userId || x.Counsellor.UserId == userId))
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
                x.CreatedAt,
                x.UpdatedAt))
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
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Session was not found.");
        if (session.ClientUserId != userId && session.Counsellor.UserId != userId)
            return EndpointHelpers.Forbidden(context);
        try { session.Cancel(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(id, userId, "SessionCancelled"));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { session.Id, session.Status }, "Session cancelled.");
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
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/sessions/{id}/rating", new { rating.Id }, "Session rated successfully.");
    }
}
