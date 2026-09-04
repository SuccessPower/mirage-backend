using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Hubs;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class MentorEndpoints
{
    public static RouteGroupBuilder MapMentorEndpoints(this RouteGroupBuilder api)
    {
        var mentors = api.MapGroup("/mentors").WithTags("Mentorship");
        mentors.MapGet("/", ListMentors);
        mentors.MapGet("/{id:guid}", GetMentor);
        mentors.MapGet("/me", GetMyProfile).RequireAuthorization(MiragePolicy.Mentor);
        mentors.MapPut("/me", UpdateMyProfile).RequireAuthorization(MiragePolicy.Mentor);
        mentors.MapPost("/apply", Apply).RequireAuthorization();

        // Paid mentorship setup: where the money lands, and what a place costs.
        mentors.MapPost("/me/bank-account", SaveBankAccount).RequireAuthorization(MiragePolicy.Mentor);
        mentors.MapPut("/me/pricing", SetPricing).RequireAuthorization(MiragePolicy.Mentor);

        // Public events. A mentor publishes onto the same /events feed a church does.
        mentors.MapPost("/me/events", CreateEvent).RequireAuthorization(MiragePolicy.Mentor);
        mentors.MapDelete("/me/events/{eventId:guid}", DeleteEvent).RequireAuthorization(MiragePolicy.Mentor);

        var requests = api.MapGroup("/mentorship/requests").WithTags("Mentorship").RequireAuthorization();
        requests.MapGet("/mine", ListMyRequests);
        requests.MapGet("/incoming", ListIncomingRequests);
        requests.MapGet("/{id:guid}", GetRequest);
        requests.MapPost("/{mentorId:guid}", SendRequest);
        requests.MapPatch("/{id:guid}/accept", AcceptRequest);
        requests.MapPatch("/{id:guid}/decline", DeclineRequest);
        requests.MapDelete("/{id:guid}", WithdrawRequest);
        // Moves an accepted mentee between the free and paid groups — the mentor's own call.
        requests.MapPatch("/{id:guid}/tier", SetMenteeTier);

        // Private channel: 1:1 messages between a mentor and one accepted mentee, keyed by the
        // MentorRequest that represents their relationship.
        requests.MapGet("/{id:guid}/messages", ListMentorMessages);
        requests.MapPost("/{id:guid}/messages", SendMentorMessage);
        requests.MapGet("/{id:guid}/meetings", ListPrivateMeetings);
        requests.MapPost("/{id:guid}/meetings", SchedulePrivateMeeting).RequireAuthorization(MiragePolicy.Mentor);
        requests.MapGet("/{id:guid}/meetings/{meetingId:guid}/video-token", GetPrivateMeetingVideoToken);

        // Broadcast group: posts, group chat, and meetings shared between a mentor and their
        // accepted mentees.
        mentors.MapGet("/{id:guid}/mentees", ListMentees).RequireAuthorization();
        mentors.MapGet("/{id:guid}/posts", ListPosts).RequireAuthorization();
        mentors.MapPost("/{id:guid}/posts", CreatePost).RequireAuthorization(MiragePolicy.Mentor);
        mentors.MapGet("/{id:guid}/group-messages", ListGroupMessages).RequireAuthorization();
        mentors.MapPost("/{id:guid}/group-messages", SendGroupMessage).RequireAuthorization();
        mentors.MapGet("/{id:guid}/meetings", ListMeetings).RequireAuthorization();
        mentors.MapPost("/{id:guid}/meetings", ScheduleMeeting).RequireAuthorization(MiragePolicy.Mentor);
        mentors.MapGet("/{id:guid}/meetings/{meetingId:guid}/video-token", GetMeetingVideoToken).RequireAuthorization();
        return api;
    }

    // A user belongs to a mentor's broadcast group if they own the mentor profile,
    // or have an Accepted MentorRequest against it.
    private static async Task<bool> IsGroupMemberAsync(Guid mentorProfileId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var isMentor = await db.Mentors.AsNoTracking()
            .AnyAsync(x => x.Id == mentorProfileId && x.UserId == userId, cancellationToken);
        if (isMentor) return true;
        return await db.MentorRequests.AsNoTracking().AnyAsync(
            x => x.MentorProfileId == mentorProfileId && x.MenteeUserId == userId &&
                 x.Status == MentorRequestStatus.Accepted, cancellationToken);
    }

    // A user is a party to a MentorRequest's private channel if they are the mentee, or they own
    // the mentor profile the request was sent to. The channel only opens once the request is accepted.
    private static async Task<bool> IsMentorRequestPartyAsync(Guid mentorRequestId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        await db.MentorRequests.AsNoTracking().AnyAsync(x => x.Id == mentorRequestId
            && (x.MenteeUserId == userId || x.Mentor.UserId == userId)
            && x.Status == MentorRequestStatus.Accepted, cancellationToken);

    // Everyone in the addressed group with an accepted request against this mentor, minus whoever
    // triggered the event. The mentor themselves is included when a mentee is the sender, so a
    // mentor hears their group talking back.
    private static async Task<List<Guid>> GroupAudienceAsync(Guid mentorProfileId, Guid exceptUserId,
        IMirageDbContext db, CancellationToken cancellationToken,
        MentorAudience audience = MentorAudience.Everyone)
    {
        var query = db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfileId && x.Status == MentorRequestStatus.Accepted);
        if (audience == MentorAudience.FreeMentees) query = query.Where(x => x.Tier == MentorshipTier.Free);
        else if (audience == MentorAudience.PaidMentees) query = query.Where(x => x.Tier == MentorshipTier.Paid);

        var mentees = await query.Select(x => x.MenteeUserId).ToListAsync(cancellationToken);
        var mentorUserId = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == mentorProfileId).Select(x => x.UserId)
            .SingleOrDefaultAsync(cancellationToken);

        return mentees.Append(mentorUserId).Distinct().Where(x => x != exceptUserId && x != Guid.Empty).ToList();
    }

    /// <summary>
    /// Which of the mentor's two groups the caller sits in, or null when the caller is the mentor
    /// — the mentor is in both and sees everything.
    /// </summary>
    private static async Task<MentorshipTier?> ViewerTierAsync(Guid mentorProfileId, Guid userId,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Mentors.AsNoTracking().AnyAsync(x => x.Id == mentorProfileId && x.UserId == userId, cancellationToken))
            return null;
        return await db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfileId && x.MenteeUserId == userId
                && x.Status == MentorRequestStatus.Accepted)
            .Select(x => (MentorshipTier?)x.Tier)
            .SingleOrDefaultAsync(cancellationToken);
    }

    // The audience label a mentee's own traffic carries, and the one extra audience they are
    // allowed to read beyond Everyone.
    private static MentorAudience AudienceFor(MentorshipTier tier) =>
        tier == MentorshipTier.Paid ? MentorAudience.PaidMentees : MentorAudience.FreeMentees;

    /// <summary>
    /// Which audiences the caller may read. A mentee reads what was addressed to everyone plus
    /// their own group. The mentor sees each group exactly as its members do — Everyone plus that
    /// group's own traffic — and the unnarrowed view is the announcement stream to both rather
    /// than a merge of all three, so the free and paid conversations stay separate rooms on the
    /// mentor's screen too.
    /// </summary>
    private static MentorAudience[] VisibleAudiences(MentorshipTier? viewerTier, MentorAudience? requested)
    {
        if (viewerTier is not null) return new[] { MentorAudience.Everyone, AudienceFor(viewerTier.Value) };
        if (requested is null or MentorAudience.Everyone) return new[] { MentorAudience.Everyone };
        return new[] { MentorAudience.Everyone, requested.Value };
    }

    // A mentor may address either group or both; a mentee only ever addresses their own.
    private static MentorAudience ResolveSendAudience(MentorshipTier? viewerTier, MentorAudience requested) =>
        viewerTier is null ? requested : AudienceFor(viewerTier.Value);

    private static async Task<IResult> ListMentorMessages(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsMentorRequestPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var visibility = await ChatVisibility.ForAsync(db, userId,
            new ChatSurface(ChatSurfaceKind.MentorRequest, id).Key, cancellationToken);
        var messages = await visibility
            .Apply(db.MentorMessages.AsNoTracking().Where(x => x.MentorRequestId == id))
            .OrderBy(x => x.CreatedAt)
            .Join(db.Profiles.AsNoTracking(), m => m.SenderId, p => p.UserId, (m, p) => new MentorMessageResponse(
                m.Id, m.MentorRequestId, m.SenderId, p.DisplayName, m.Content, m.Type, m.AttachmentUrl, m.CreatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, messages, "Messages retrieved successfully.");
    }

    private static async Task<IResult> SendMentorMessage(Guid id, SendMentorMessageRequest request,
        HttpContext context, IMirageDbContext db, IHubContext<ChatHub> hub, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsMentorRequestPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Message content is required."));

        var message = new MentorMessage(id, userId, request.Content, request.Type, request.AttachmentUrl);
        db.MentorMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await hub.Clients.Group($"mentorrequest:{id}").SendAsync("ReceiveMentorMessage", new
        {
            message.Id,
            MentorRequestId = id,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        }, cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentorship/requests/{id}/messages/{message.Id}",
            new { message.Id }, "Message sent successfully.");
    }

    private static async Task<IResult> ListPrivateMeetings(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsMentorRequestPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        var meetings = await db.MentorMeetings.AsNoTracking()
            .Where(x => x.MentorRequestId == id)
            .OrderBy(x => x.ScheduledAt)
            // A 1:1 meeting has exactly one audience — the mentee it belongs to — so Audience is
            // passed explicitly rather than defaulted (an expression tree cannot omit it).
            .Select(x => new MentorMeetingResponse(x.Id, x.MentorProfileId, x.ScheduledByUserId, x.Title,
                x.MeetingLink, x.ScheduledAt, x.DurationMinutes, MentorAudience.Everyone))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, meetings, "Private meetings retrieved successfully.");
    }

    private static async Task<IResult> SchedulePrivateMeeting(Guid id, ScheduleMentorMeetingRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var relationship = await db.MentorRequests.AsNoTracking()
            .Where(x => x.Id == id && x.Mentor.UserId == userId && x.Status == MentorRequestStatus.Accepted)
            .Select(x => new { x.MentorProfileId, x.MenteeUserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (relationship is null) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.MeetingLink))
            return EndpointHelpers.ValidationProblem(context, ("meeting", "Title and meeting link are required."));

        var meeting = new MentorMeeting(relationship.MentorProfileId, userId, request.Title, request.MeetingLink,
            request.ScheduledAt, request.DurationMinutes, id);
        db.MentorMeetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);
        await notifications.NotifyAsync(relationship.MenteeUserId, NotificationType.SessionBooked,
            "One-to-one call scheduled", $"{request.Title} was scheduled for {request.ScheduledAt:MMM d, h:mm tt}.",
            id, "MentorRequest", cancellationToken);
        return ApiResults.Created(context, $"/api/v1/mentorship/requests/{id}/meetings/{meeting.Id}",
            new { meeting.Id }, "Private meeting scheduled successfully.");
    }

    private static async Task<IResult> GetPrivateMeetingVideoToken(Guid id, Guid meetingId, HttpContext context,
        MirageDbContext db, JitsiService jitsi, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsMentorRequestPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        var meeting = await db.MentorMeetings.AsNoTracking()
            .Where(x => x.Id == meetingId && x.MentorRequestId == id)
            .Select(x => new { x.MentorProfileId })
            .SingleOrDefaultAsync(cancellationToken);
        if (meeting is null) return EndpointHelpers.NotFound(context, "Meeting was not found.");
        var mentorUserId = await db.Mentors.AsNoTracking().Where(x => x.Id == meeting.MentorProfileId)
            .Select(x => x.UserId).SingleAsync(cancellationToken);
        var displayName = await db.Profiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "Mirage member";
        var email = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);
        var room = $"mirage-mentor-private-{id:N}-{meetingId:N}";
        var token = jitsi.CreateToken(userId, displayName, email, room, mentorUserId == userId);
        return ApiResults.Ok(context, new { AppId = jitsi.AppId, Room = room, Token = token },
            "Video token issued successfully.");
    }

    private static async Task<IResult> ListMentees(Guid id, HttpContext context, IMirageDbContext db,
        MentorAudience? audience, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var isMentor = await db.Mentors.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        var allowMenteesToSeeEachOther = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == id).Select(x => x.AllowMenteesToSeeEachOther).SingleAsync(cancellationToken);

        // A mentee only ever sees their own group's roster; the mentor sees both, and can narrow
        // to one with ?audience=.
        var viewerTier = await ViewerTierAsync(id, userId, db, cancellationToken);
        var query = db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == id && x.Status == MentorRequestStatus.Accepted);
        if (viewerTier is not null) query = query.Where(x => x.Tier == viewerTier);
        else if (audience is MentorAudience.FreeMentees) query = query.Where(x => x.Tier == MentorshipTier.Free);
        else if (audience is MentorAudience.PaidMentees) query = query.Where(x => x.Tier == MentorshipTier.Paid);

        var mentees = await query
            // Left join, not inner: a mentee with no profile row must still appear on the roster
            // rather than disappearing from their mentor's list entirely.
            .Select(r => new
            {
                r.Id,
                r.MenteeUserId,
                DisplayName = db.Profiles.Where(p => p.UserId == r.MenteeUserId)
                    .Select(p => p.DisplayName).FirstOrDefault(),
                AvatarUrl = db.Profiles.Where(p => p.UserId == r.MenteeUserId)
                    .Select(p => p.AvatarUrl).FirstOrDefault(),
                r.Tier,
                AcceptedAt = r.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var badges = await db.GetOrgBadgesAsync(mentees.Select(x => x.MenteeUserId), cancellationToken);
        var result = mentees.Select(x => isMentor || x.MenteeUserId == userId || allowMenteesToSeeEachOther
            ? new MentorMenteeResponse(x.Id, x.MenteeUserId, x.DisplayName ?? "Mentee", x.AvatarUrl, x.AcceptedAt,
                badges.GetValueOrDefault(x.MenteeUserId)?.LogoUrl,
                badges.GetValueOrDefault(x.MenteeUserId)?.OrganisationName, x.Tier)
            : new MentorMenteeResponse(x.Id, x.MenteeUserId, "Fellow mentee", null, x.AcceptedAt, Tier: x.Tier));

        return ApiResults.Ok(context, result, "Mentees retrieved successfully.");
    }

    private static async Task<IResult> ListPosts(Guid id, HttpContext context, IMirageDbContext db,
        MentorAudience? audience, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        // A mentee sees what was addressed to everyone plus their own group's posts and nothing
        // else; the mentor (null tier) sees both groups, optionally narrowed by ?audience=.
        var viewerTier = await ViewerTierAsync(id, userId, db, cancellationToken);
        var query = db.MentorPosts.AsNoTracking().Where(x => x.MentorProfileId == id);
        var visible = VisibleAudiences(viewerTier, audience);
        query = query.Where(x => visible.Contains(x.Audience));

        var posts = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MentorPostResponse(x.Id, x.MentorProfileId, x.Content, x.ImageUrl, x.CreatedAt,
                x.Audience))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, posts, "Posts retrieved successfully.");
    }

    private static async Task<IResult> CreatePost(Guid id, CreateMentorPostRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var isMentor = await db.Mentors.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (!isMentor) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Post content is required."));

        var post = new MentorPost(id, request.Content, request.ImageUrl, request.Audience);
        db.MentorPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        // A post is the mentor speaking to a group, so every mentee in that group is told — in-app
        // and on their phone. Without this a post only reached whoever happened to have the group
        // screen open.
        var mentorName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken)
            ?? "Your mentor";
        var preview = request.Content.Length > 120 ? request.Content[..120].TrimEnd() + "…" : request.Content;
        foreach (var menteeId in await GroupAudienceAsync(id, userId, db, cancellationToken, request.Audience))
            await notifications.NotifyAsync(menteeId, NotificationType.MentorGroupPost,
                $"{mentorName} posted to your group", preview, id, "MentorProfile", cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentors/{id}/posts/{post.Id}", new { post.Id }, "Post published successfully.");
    }

    private static async Task<IResult> ListGroupMessages(Guid id, HttpContext context, IMirageDbContext db,
        MentorAudience? audience, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.UserId, x.AllowMenteesToSeeEachOther })
            .SingleAsync(cancellationToken);
        var isMentor = mentor.UserId == userId;

        // Free and paid mentees hold separate conversations: each reads Everyone plus their own
        // group. The mentor reads both, and can narrow to one with ?audience=.
        var viewerTier = await ViewerTierAsync(id, userId, db, cancellationToken);
        var visibility = await ChatVisibility.ForAsync(db, userId,
            new ChatSurface(ChatSurfaceKind.MentorGroup, id).Key, cancellationToken);
        var query = visibility
            .Apply(db.MentorGroupMessages.AsNoTracking().Where(x => x.MentorProfileId == id));
        var visible = VisibleAudiences(viewerTier, audience);
        query = query.Where(x => visible.Contains(x.Audience));

        var messages = await query
            .OrderBy(x => x.CreatedAt)
            // Left join, not inner: a sender whose profile row is missing must not silently erase
            // their message from the conversation.
            .Select(m => new MentorGroupMessageResponse(
                m.Id, m.MentorProfileId, m.SenderId,
                db.Profiles.Where(p => p.UserId == m.SenderId).Select(p => p.DisplayName).FirstOrDefault()
                    ?? "Member",
                m.Content, m.Type, m.AttachmentUrl, m.CreatedAt, m.Audience))
            .ToListAsync(cancellationToken);

        // Mentees can't see who a fellow mentee is unless the mentor opts in; the mentor and each
        // mentee's own messages always show their real name.
        if (!isMentor && !mentor.AllowMenteesToSeeEachOther)
            messages = messages
                .Select(m => m.SenderId == userId || m.SenderId == mentor.UserId
                    ? m
                    : m with { SenderName = "Fellow mentee" })
                .ToList();

        return ApiResults.Ok(context, messages, "Group messages retrieved successfully.");
    }

    private static async Task<IResult> SendGroupMessage(Guid id, SendMentorGroupMessageRequest request,
        HttpContext context, IMirageDbContext db, IHubContext<ChatHub> hub,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Message content is required."));

        // A mentee can only ever speak into their own group; only the mentor picks an audience.
        var viewerTier = await ViewerTierAsync(id, userId, db, cancellationToken);
        var sendAudience = ResolveSendAudience(viewerTier, request.Audience);

        var message = new MentorGroupMessage(id, userId, request.Content, request.Type, request.AttachmentUrl,
            sendAudience);
        db.MentorGroupMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        // The hub group is per audience, so a paid-group message never lands on a free mentee's
        // open screen. Everyone fans out to both plus the shared room.
        var hubGroups = sendAudience == MentorAudience.Everyone
            ? new[] { $"mentorgroup:{id}", $"mentorgroup:{id}:free", $"mentorgroup:{id}:paid" }
            : [$"mentorgroup:{id}:{(sendAudience == MentorAudience.PaidMentees ? "paid" : "free")}"];
        await hub.Clients.Groups(hubGroups).SendAsync("ReceiveMentorGroupMessage", new
        {
            message.Id,
            MentorProfileId = id,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            message.Audience,
            SentAt = message.CreatedAt
        }, cancellationToken);

        // The hub broadcast above only reaches members with the group screen open. Everyone else
        // needs a real notification, or the conversation is invisible until they happen to look.
        var messagePreview = request.Content.Length > 120
            ? request.Content[..120].TrimEnd() + "…"
            : request.Content;
        foreach (var memberId in await GroupAudienceAsync(id, userId, db, cancellationToken, sendAudience))
            await notifications.NotifyAsync(memberId, NotificationType.MentorGroupMessage,
                $"{senderName ?? "Someone"} messaged your group", messagePreview, id, "MentorProfile",
                cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentors/{id}/group-messages/{message.Id}",
            new { message.Id }, "Message sent successfully.");
    }

    private static async Task<IResult> ListMeetings(Guid id, HttpContext context, IMirageDbContext db,
        MentorAudience? audience, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var viewerTier = await ViewerTierAsync(id, userId, db, cancellationToken);
        var query = db.MentorMeetings.AsNoTracking()
            .Where(x => x.MentorProfileId == id && x.MentorRequestId == null);
        var visible = VisibleAudiences(viewerTier, audience);
        query = query.Where(x => visible.Contains(x.Audience));

        var meetings = await query
            .OrderBy(x => x.ScheduledAt)
            .Select(x => new MentorMeetingResponse(x.Id, x.MentorProfileId, x.ScheduledByUserId, x.Title,
                x.MeetingLink, x.ScheduledAt, x.DurationMinutes, x.Audience))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, meetings, "Meetings retrieved successfully.");
    }

    private static async Task<IResult> ScheduleMeeting(Guid id, ScheduleMentorMeetingRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var isMentor = await db.Mentors.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (!isMentor) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.MeetingLink))
            return EndpointHelpers.ValidationProblem(context, ("meeting", "Title and meeting link are required."));

        var meeting = new MentorMeeting(id, userId, request.Title, request.MeetingLink, request.ScheduledAt,
            request.DurationMinutes, null, request.Audience);
        db.MentorMeetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);

        // Only the group the meeting was scheduled for hears about it.
        var menteeIds = await GroupAudienceAsync(id, userId, db, cancellationToken, request.Audience);
        foreach (var menteeId in menteeIds)
            await notifications.NotifyAsync(menteeId, NotificationType.SessionBooked, "New meeting scheduled",
                $"{request.Title} was scheduled for {request.ScheduledAt:MMM d, h:mm tt}.", meeting.Id, "MentorMeeting",
                cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentors/{id}/meetings/{meeting.Id}", new { meeting.Id },
            "Meeting scheduled successfully.");
    }

    private static async Task<IResult> GetMeetingVideoToken(Guid id, Guid meetingId, HttpContext context,
        MirageDbContext db, JitsiService jitsi, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        var meetingAudience = await db.MentorMeetings.AsNoTracking()
            .Where(x => x.Id == meetingId && x.MentorProfileId == id && x.MentorRequestId == null)
            .Select(x => (MentorAudience?)x.Audience)
            .SingleOrDefaultAsync(cancellationToken);
        if (meetingAudience is null) return EndpointHelpers.NotFound(context, "Meeting was not found.");

        // A free mentee must not be handed a token for the paid group's call, or the other way
        // round — the split is only real if it holds at the video room door too.
        var viewerTier = await ViewerTierAsync(id, userId, db, cancellationToken);
        if (viewerTier is not null && meetingAudience != MentorAudience.Everyone
            && meetingAudience != AudienceFor(viewerTier.Value))
            return EndpointHelpers.Forbidden(context, "This meeting is for the other mentorship group.");

        var mentorUserId = await db.Mentors.AsNoTracking().Where(x => x.Id == id)
            .Select(x => x.UserId).SingleAsync(cancellationToken);
        var displayName = await db.Profiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "Mirage member";
        var email = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);
        var room = $"mirage-mentor-{id:N}-{meetingId:N}";
        var token = jitsi.CreateToken(userId, displayName, email, room, mentorUserId == userId);
        return ApiResults.Ok(context, new { AppId = jitsi.AppId, Room = room, Token = token },
            "Video token issued successfully.");
    }

    private static async Task<IResult> ListMentors(HttpContext context, IMirageDbContext db,
        UserManager<ApplicationUser> userManager, string? denomination, string? areaOfGuidance, bool freeOnly = false,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var currentUserId = context.User.TryGetUserId();
        var query = db.Mentors.AsNoTracking()
            .Where(x => x.IsApproved && userManager.Users.Any(u => u.Id == x.UserId && u.IsActive));
        if (currentUserId is not null) query = query.Where(x => x.UserId != currentUserId);
        if (freeOnly) query = query.Where(x => x.AcceptsFreeSessions);
        if (!string.IsNullOrWhiteSpace(denomination))
            query = query.Where(x => EF.Functions.ILike(x.UserProfile.Denomination, $"%{denomination.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(areaOfGuidance))
            query = query.Where(x => x.AreasOfGuidance.Any(a => EF.Functions.ILike(a, $"%{areaOfGuidance.Trim()}%")));

        var result = query.OrderByDescending(x => x.YearsMarried).Select(x => new
        {
            x.Id,
            DisplayName = x.UserProfile.DisplayName,
            x.UserProfile.Denomination,
            x.UserProfile.City,
            x.UserProfile.AvatarUrl,
            x.YearsMarried,
            x.AcceptsFreeSessions,
            x.AreasOfGuidance,
            x.Languages,
            x.OffersPaidMentorship,
            x.PriceAmount,
            x.PriceCurrency,
            AcceptsPaidMentees = x.OffersPaidMentorship && x.BankCode != null && x.BankAccountNumber != null
                && x.PriceAmount > 0 && x.PriceCurrency != null,
        });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Mentors retrieved successfully.");
    }

    private static async Task<IResult> GetMentor(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var currentUserId = context.User.TryGetUserId();

        // The viewer's own relationship with this mentor, resolved here rather than left to the
        // client to find by paging its request list — a mentee who already has this mentor must
        // never be offered "Request mentorship" again, and that must not depend on how many
        // requests they happen to have.
        var myRequest = currentUserId is null
            ? null
            : await db.MentorRequests.AsNoTracking()
                .Where(x => x.MentorProfileId == id && x.MenteeUserId == currentUserId)
                .Select(x => new { x.Id, x.Status, x.Tier })
                .SingleOrDefaultAsync(cancellationToken);
        var isAcceptedMentee = myRequest is { Status: MentorRequestStatus.Accepted };

        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == id && x.IsApproved)
            .Select(x => new
            {
                x.Id,
                DisplayName = x.UserProfile.DisplayName,
                x.UserProfile.Denomination,
                x.UserProfile.City,
                x.UserProfile.AvatarUrl,
                x.YearsMarried,
                x.Testimony,
                x.AcceptsFreeSessions,
                x.AreasOfGuidance,
                x.Languages,
                PhoneNumber = isAcceptedMentee ? x.PhoneNumber : null,
                // What a mentee needs in order to choose a group before asking.
                x.OffersPaidMentorship,
                x.PriceAmount,
                x.PriceCurrency,
                AcceptsPaidMentees = x.OffersPaidMentorship && x.BankCode != null && x.BankAccountNumber != null
                    && x.PriceAmount > 0 && x.PriceCurrency != null,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (mentor is null) return EndpointHelpers.NotFound(context, "Mentor was not found.");

        return ApiResults.Ok(context, new
        {
            mentor.Id, mentor.DisplayName, mentor.Denomination, mentor.City, mentor.AvatarUrl,
            mentor.YearsMarried, mentor.Testimony, mentor.AcceptsFreeSessions,
            mentor.AreasOfGuidance, mentor.Languages, mentor.PhoneNumber,
            mentor.OffersPaidMentorship, mentor.PriceAmount, mentor.PriceCurrency,
            mentor.AcceptsPaidMentees,
            // Null when the viewer has no relationship with this mentor at all.
            MyRequestId = myRequest?.Id,
            MyRequestStatus = myRequest?.Status,
            MyTier = myRequest?.Tier,
        }, "Mentor retrieved successfully.");
    }

    private static async Task<IResult> GetMyProfile(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Id, x.UserId, x.YearsMarried, x.Testimony,
                x.IsApproved, x.AcceptsFreeSessions, x.AllowMenteesToSeeEachOther,
                x.AreasOfGuidance, x.Languages, x.PhoneNumber, x.CreatedAt,
                x.OffersPaidMentorship, x.PriceAmount, x.PriceCurrency,
                x.BankName, x.BankAccountName, x.BankAccountNumber,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Mentor profile was not found.");
        return ApiResults.Ok(context, new
        {
            profile.Id, profile.UserId, profile.YearsMarried, profile.Testimony,
            profile.IsApproved, profile.AcceptsFreeSessions, profile.AllowMenteesToSeeEachOther,
            profile.AreasOfGuidance, profile.Languages, profile.PhoneNumber, profile.CreatedAt,
            profile.OffersPaidMentorship, profile.PriceAmount, profile.PriceCurrency,
            profile.BankName, profile.BankAccountName,
            // Never the full number back out — enough to recognise, never enough to use.
            BankAccountNumberMasked = PracticeEndpoints.MaskAccountNumber(profile.BankAccountNumber),
            HasPayoutAccount = profile.BankAccountNumber is not null,
        }, "Mentor profile retrieved successfully.");
    }

    private static async Task<IResult> Apply(ApplyMentorRequest request, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.YearsMarried < 1)
            return EndpointHelpers.ValidationProblem(context, ("yearsMarried", "At least 1 year of marriage is required."));
        if (string.IsNullOrWhiteSpace(request.Testimony))
            return EndpointHelpers.ValidationProblem(context, ("testimony", "Testimony is required."));

        var userId = context.User.GetUserId();
        if (await db.Mentors.AnyAsync(x => x.UserId == userId, cancellationToken))
            return EndpointHelpers.Conflict(context, "You already have a mentor application on file.");

        var profile = new MentorProfile(userId, request.YearsMarried, request.Testimony, request.AreasOfGuidance,
            request.Languages);
        db.Mentors.Add(profile);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentors/{profile.Id}", new { profile.Id },
            "Mentor application submitted! An admin will review your profile before it appears publicly.");
    }

    private static async Task<IResult> UpdateMyProfile(UpdateMentorProfileRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.YearsMarried < 1)
            return EndpointHelpers.ValidationProblem(context, ("yearsMarried", "At least 1 year of marriage is required."));
        if (string.IsNullOrWhiteSpace(request.Testimony))
            return EndpointHelpers.ValidationProblem(context, ("testimony", "Testimony is required."));

        var userId = context.User.GetUserId();
        var profile = await db.Mentors.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Mentor profile was not found.");
        profile.UpdateProfile(request.YearsMarried, request.Testimony, request.AreasOfGuidance, request.Languages,
            request.AcceptsFreeSessions, request.AllowMenteesToSeeEachOther);
        profile.SetPhoneNumber(request.PhoneNumber);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.Id }, "Mentor profile updated successfully.");
    }

    private static async Task<IResult> SendRequest(Guid mentorId, RequestMentorRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return EndpointHelpers.ValidationProblem(context, ("message", "A message is required."));

        var userId = context.User.GetUserId();
        if (!await db.Profiles.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsVerified, cancellationToken))
            return EndpointHelpers.Forbidden(context, "Verify your profile before requesting mentorship.");

        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == mentorId && x.IsApproved)
            .Select(x => new
            {
                x.UserId, x.AcceptsFreeSessions, x.OffersPaidMentorship,
                x.PriceAmount, x.PriceCurrency, x.BankCode, x.BankAccountNumber,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (mentor is null)
            return EndpointHelpers.NotFound(context, "Approved mentor was not found.");
        if (mentor.UserId == userId)
            return EndpointHelpers.ValidationProblem(context, ("mentorId", "You cannot request your own mentorship."));

        var canCharge = mentor.OffersPaidMentorship && mentor.BankCode is not null
            && mentor.BankAccountNumber is not null && mentor.PriceAmount > 0 && mentor.PriceCurrency is not null;
        if (request.Tier == MentorshipTier.Paid && !canCharge)
            return EndpointHelpers.ValidationProblem(context,
                ("tier", "This mentor is not currently accepting paid mentees."));
        if (request.Tier == MentorshipTier.Free && !mentor.AcceptsFreeSessions)
            return EndpointHelpers.ValidationProblem(context,
                ("tier", "This mentor is not currently accepting free mentees."));

        // A mentor and a mentee only ever have one relationship row — the unique index on
        // (MentorProfileId, MenteeUserId) enforces it. Someone who was declined or withdrew and
        // now asks again reopens that row; inserting a second one used to fail the index and
        // surface as a 500, which is why re-requests after a decline never reached the mentor.
        var mentorRequest = await db.MentorRequests
            .SingleOrDefaultAsync(x => x.MentorProfileId == mentorId && x.MenteeUserId == userId, cancellationToken);
        if (mentorRequest is null)
        {
            mentorRequest = new MentorRequest(mentorId, userId, request.Message, request.Tier);
            db.MentorRequests.Add(mentorRequest);
        }
        else
        {
            switch (mentorRequest.Status)
            {
                case MentorRequestStatus.Pending:
                    return EndpointHelpers.Conflict(context, "You already have a pending request to this mentor.");
                case MentorRequestStatus.Accepted:
                    return EndpointHelpers.Conflict(context, "This mentor is already mentoring you.");
                case MentorRequestStatus.AwaitingPayment when request.Tier == MentorshipTier.Paid:
                    break; // Checkout was abandoned; re-issue a payment against the same row below.
                default:
                    mentorRequest.Reopen(request.Message, request.Tier);
                    break;
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var menteeName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken)
            ?? "A member";

        // A paid place is not a request until it is funded. The mentor is told by the payment
        // webhook (PaymentEndpoints.ConfirmMentorshipPaymentAsync), not here — otherwise their
        // inbox fills with places nobody ever paid for.
        if (request.Tier == MentorshipTier.Paid)
        {
            var existingPayment = await db.Payments
                .Where(x => x.MentorRequestId == mentorRequest.Id && x.Status == PaymentStatus.Pending)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            var payment = existingPayment ?? Payment.ForMentorship(mentorRequest.Id, userId, mentorId,
                mentor.PriceAmount!.Value, mentor.PriceCurrency!);
            if (existingPayment is null) db.Payments.Add(payment);
            await db.SaveChangesAsync(cancellationToken);

            return ApiResults.Created(context, $"/api/v1/mentorship/requests/{mentorRequest.Id}",
                new { mentorRequest.Id, mentorRequest.Status, mentorRequest.Tier, PaymentId = payment.Id,
                    payment.Amount, payment.Currency },
                "Complete payment to send your request to this mentor.");
        }

        await notifications.NotifyAsync(mentor.UserId, NotificationType.MentorRequestReceived,
            "New mentorship request", $"{menteeName} requested your mentorship.",
            mentorRequest.Id, "MentorRequest", cancellationToken, "/practice/mentorship");

        return ApiResults.Created(context, $"/api/v1/mentorship/requests/{mentorRequest.Id}",
            new { mentorRequest.Id, mentorRequest.Status, mentorRequest.Tier },
            "Mentor request sent successfully.");
    }

    /// <summary>Moves an accepted mentee between the mentor's free and paid groups.</summary>
    private static async Task<IResult> SetMenteeTier(Guid id, SetMenteeTierRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var mentorProfileId = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (mentorProfileId is null) return EndpointHelpers.Forbidden(context, "Only mentors can move a mentee.");

        var mentorRequest = await db.MentorRequests
            .SingleOrDefaultAsync(x => x.Id == id && x.MentorProfileId == mentorProfileId, cancellationToken);
        if (mentorRequest is null) return EndpointHelpers.NotFound(context, "Mentor request was not found.");
        if (mentorRequest.Status != MentorRequestStatus.Accepted)
            return EndpointHelpers.Conflict(context, "Only an accepted mentee can be moved between groups.");

        mentorRequest.SetTier(request.Tier);
        await db.SaveChangesAsync(cancellationToken);

        var groupName = request.Tier == MentorshipTier.Paid ? "paid" : "free";
        await notifications.NotifyAsync(mentorRequest.MenteeUserId, NotificationType.MentorRequestAccepted,
            "Your mentorship group changed",
            $"Your mentor moved you into their {groupName} mentorship group.",
            mentorRequest.Id, "MentorRequest", cancellationToken);

        return ApiResults.Ok(context, new { mentorRequest.Id, mentorRequest.Tier }, "Mentee group updated.");
    }

    private static async Task<IResult> ListMyRequests(HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var query = db.MentorRequests.AsNoTracking()
            .Where(x => x.MenteeUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.MentorProfileId,
                MentorName = x.Mentor.UserProfile.DisplayName,
                x.Message,
                x.Status,
                x.CreatedAt
            });
        return ApiResults.Ok(context,
            await query.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Mentor requests retrieved successfully.");
    }

    private static async Task<IResult> ListIncomingRequests(HttpContext context, IMirageDbContext db,
        MentorRequestStatus? status, MentorshipTier? tier, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var mentorProfile = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (mentorProfile == Guid.Empty)
            return EndpointHelpers.NotFound(context, "Mentor profile was not found.");

        var query = db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfile);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (tier.HasValue) query = query.Where(x => x.Tier == tier.Value);

        var result = query
            // Left join, not inner: this was an inner join on Profiles, so a request from a mentee
            // whose profile row was missing was dropped from the result set with no error — the
            // request existed in the database but never appeared in the mentor's inbox.
            .Select(r => new
            {
                r.Id,
                r.MenteeUserId,
                MenteeName = db.Profiles.Where(p => p.UserId == r.MenteeUserId)
                    .Select(p => p.DisplayName).FirstOrDefault() ?? "Member",
                MenteeAvatarUrl = db.Profiles.Where(p => p.UserId == r.MenteeUserId)
                    .Select(p => p.AvatarUrl).FirstOrDefault(),
                r.Message,
                r.Status,
                r.Tier,
                r.PaidAt,
                r.CreatedAt
            })
            .OrderByDescending(x => x.CreatedAt);
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Incoming mentor requests retrieved successfully.");
    }

    private static async Task<IResult> GetRequest(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var request = await db.MentorRequests.AsNoTracking()
            .Where(x => x.Id == id && (x.MenteeUserId == userId || x.Mentor.UserId == userId))
            .Select(x => new
            {
                x.Id,
                x.MentorProfileId,
                MentorUserId = x.Mentor.UserId,
                MentorName = x.Mentor.UserProfile.DisplayName,
                MentorAvatarUrl = x.Mentor.UserProfile.AvatarUrl,
                MentorPhoneNumber = x.Status == MentorRequestStatus.Accepted ? x.Mentor.PhoneNumber : null,
                x.MenteeUserId,
                x.Message,
                x.Status,
                x.Tier,
                x.PaidAt,
                x.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Mentor request was not found.");

        var mentee = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == request.MenteeUserId)
            .Select(x => new { x.DisplayName, x.AvatarUrl })
            .SingleOrDefaultAsync(cancellationToken);
        var badges = await db.GetOrgBadgesAsync([request.MentorUserId, request.MenteeUserId], cancellationToken);
        var mentorBadge = badges.GetValueOrDefault(request.MentorUserId);
        var menteeBadge = badges.GetValueOrDefault(request.MenteeUserId);

        var response = new MentorRequestDetailResponse(request.Id, request.MentorProfileId, request.MentorUserId,
            request.MentorName, request.MentorAvatarUrl, request.MenteeUserId, mentee?.DisplayName ?? "Mentee",
            mentee?.AvatarUrl, request.Message, request.Status, request.CreatedAt, request.MentorPhoneNumber,
            mentorBadge?.LogoUrl, mentorBadge?.OrganisationName, menteeBadge?.LogoUrl, menteeBadge?.OrganisationName,
            request.Tier, request.PaidAt);
        return ApiResults.Ok(context, response, "Mentor request retrieved successfully.");
    }

    private static async Task<IResult> AcceptRequest(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var mentorProfileId = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);

        var request = await db.MentorRequests
            .SingleOrDefaultAsync(x => x.Id == id && x.MentorProfileId == mentorProfileId, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Mentor request was not found.");
        if (request.Status == MentorRequestStatus.AwaitingPayment)
            return EndpointHelpers.Conflict(context,
                "This mentee has not completed payment for their place yet.");
        if (request.Status != MentorRequestStatus.Pending)
            return EndpointHelpers.Conflict(context, "Only pending requests can be accepted.");
        request.Accept();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(request.MenteeUserId, NotificationType.MentorRequestAccepted,
            "Mentorship request accepted", "Your mentorship request was accepted.",
            request.Id, "MentorRequest", cancellationToken);

        return ApiResults.Ok(context, new { request.Id, request.Status }, "Mentor request accepted.");
    }

    private static async Task<IResult> DeclineRequest(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var mentorProfileId = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);

        var request = await db.MentorRequests
            .SingleOrDefaultAsync(x => x.Id == id && x.MentorProfileId == mentorProfileId, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Mentor request was not found.");
        if (request.Status != MentorRequestStatus.Pending)
            return EndpointHelpers.Conflict(context, "Only pending requests can be declined.");
        request.Decline();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(request.MenteeUserId, NotificationType.MentorRequestDeclined,
            "Mentorship request declined", "Your mentorship request was declined.",
            request.Id, "MentorRequest", cancellationToken);

        return ApiResults.Ok(context, new { request.Id, request.Status }, "Mentor request declined.");
    }

    private static async Task<IResult> WithdrawRequest(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var request = await db.MentorRequests
            .SingleOrDefaultAsync(x => x.Id == id && x.MenteeUserId == userId, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Mentor request was not found.");
        // AwaitingPayment counts as withdrawable: it is a checkout the mentee started and can
        // change their mind about before paying.
        if (request.Status is not (MentorRequestStatus.Pending or MentorRequestStatus.AwaitingPayment))
            return EndpointHelpers.Conflict(context, "Only pending requests can be withdrawn.");
        request.Withdraw();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { request.Id, request.Status }, "Mentor request withdrawn.");
    }

    // ------------------------------------------------------------ paid mentorship

    /// <summary>
    /// Stores the mentor's verified payout destination and the reusable Paystack transfer
    /// recipient. Mirrors the counsellor flow — a practitioner cannot charge until there is
    /// somewhere for the money to settle.
    /// </summary>
    private static async Task<IResult> SaveBankAccount(SaveBankAccountRequest request, HttpContext context,
        IMirageDbContext db, PaystackService paystack, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BankCode) || string.IsNullOrWhiteSpace(request.AccountNumber)
            || string.IsNullOrWhiteSpace(request.AccountName))
            return EndpointHelpers.ValidationProblem(context,
                ("accountNumber", "Bank, account number, and account name are required."));

        var userId = context.User.GetUserId();
        var profile = await db.Mentors.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Mentor profile was not found.");

        profile.SetBankAccount(request.BankCode, request.BankName, request.AccountNumber, request.AccountName);

        try
        {
            var recipientCode = await paystack.CreateTransferRecipientAsync(request.AccountName, request.BankCode,
                request.AccountNumber, "NGN", cancellationToken);
            profile.SetPaystackTransferRecipientCode(recipientCode);
        }
        catch (Exception)
        {
            return EndpointHelpers.Problem(context, StatusCodes.Status502BadGateway,
                "Payout setup failed", "The bank account was resolved, but the payout recipient could not be created.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context,
            new { profile.Id, profile.BankName, profile.BankAccountName, HasPayoutAccount = true },
            "Payout account saved successfully.");
    }

    /// <summary>Opens or closes the paid group and sets what a place in it costs.</summary>
    private static async Task<IResult> SetPricing(SetMentorPricingRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Mentors.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Mentor profile was not found.");

        try
        {
            profile.SetPaidMentorship(request.OffersPaidMentorship, request.PriceAmount, request.PriceCurrency);
        }
        catch (InvalidOperationException ex)
        {
            // The domain refuses to open a paid group with nowhere for the money to land, so the
            // mentee never reaches a checkout that cannot settle.
            return EndpointHelpers.ValidationProblem(context, ("pricing", ex.Message));
        }

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new MentorPricingResponse(
            profile.OffersPaidMentorship, profile.PriceAmount, profile.PriceCurrency, profile.HasPayoutAccount,
            profile.BankName, profile.BankAccountName, PracticeEndpoints.MaskAccountNumber(profile.BankAccountNumber),
            profile.CanChargeForMentorship), "Mentorship pricing updated successfully.");
    }

    // ------------------------------------------------------------ public events

    /// <summary>
    /// Publishes a mentor's event onto the same public /events feed churches use. The event is
    /// public to everyone regardless of audience; Audience only decides which of the mentor's two
    /// groups is notified about it first.
    /// </summary>
    private static async Task<IResult> CreateEvent(CreateMentorEventRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return EndpointHelpers.ValidationProblem(context, ("title", "An event title is required."));
        if (string.IsNullOrWhiteSpace(request.Location))
            return EndpointHelpers.ValidationProblem(context, ("location", "An event location is required."));
        if (request.EndsAt <= request.StartsAt)
            return EndpointHelpers.ValidationProblem(context, ("endsAt", "The event must end after it starts."));
        if (request.Capacity is <= 0)
            return EndpointHelpers.ValidationProblem(context, ("capacity", "Capacity must be greater than zero."));

        var userId = context.User.GetUserId();
        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.IsApproved })
            .SingleOrDefaultAsync(cancellationToken);
        if (mentor is null) return EndpointHelpers.NotFound(context, "Mentor profile was not found.");
        // An unapproved mentor is invisible on the public feed anyway; refusing here says why.
        if (!mentor.IsApproved)
            return EndpointHelpers.Forbidden(context, "Your mentor profile must be approved before you can publish events.");

        var orgEvent = OrgEvent.ForMentor(mentor.Id, userId, request.Title, request.Description, request.ImageUrl,
            request.StartsAt, request.EndsAt, request.Location, request.Capacity, request.Audience);
        db.OrgEvents.Add(orgEvent);
        await db.SaveChangesAsync(cancellationToken);

        var mentorName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken)
            ?? "Your mentor";
        foreach (var menteeId in await GroupAudienceAsync(mentor.Id, userId, db, cancellationToken, request.Audience))
            await notifications.NotifyAsync(menteeId, NotificationType.MentorEventPublished,
                $"{mentorName} is hosting an event",
                $"{request.Title} — {request.StartsAt:MMM d, h:mm tt} at {request.Location}.",
                orgEvent.Id, "OrgEvent", cancellationToken, $"/events/{orgEvent.Id}");

        return ApiResults.Created(context, $"/api/v1/events/{orgEvent.Id}", new { orgEvent.Id },
            "Event published successfully.");
    }

    private static async Task<IResult> DeleteEvent(Guid eventId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var mentorProfileId = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (mentorProfileId is null) return EndpointHelpers.NotFound(context, "Mentor profile was not found.");

        var orgEvent = await db.OrgEvents
            .SingleOrDefaultAsync(x => x.Id == eventId && x.MentorProfileId == mentorProfileId, cancellationToken);
        if (orgEvent is null) return EndpointHelpers.NotFound(context, "Event was not found.");

        db.OrgEvents.Remove(orgEvent);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { orgEvent.Id }, "Event removed successfully.");
    }
}
