using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Hubs;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

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

        var requests = api.MapGroup("/mentorship/requests").WithTags("Mentorship").RequireAuthorization();
        requests.MapGet("/mine", ListMyRequests);
        requests.MapGet("/incoming", ListIncomingRequests);
        requests.MapGet("/{id:guid}", GetRequest);
        requests.MapPost("/{mentorId:guid}", SendRequest);
        requests.MapPatch("/{id:guid}/accept", AcceptRequest);
        requests.MapPatch("/{id:guid}/decline", DeclineRequest);
        requests.MapDelete("/{id:guid}", WithdrawRequest);

        // Private channel: 1:1 messages between a mentor and one accepted mentee, keyed by the
        // MentorRequest that represents their relationship.
        requests.MapGet("/{id:guid}/messages", ListMentorMessages);
        requests.MapPost("/{id:guid}/messages", SendMentorMessage);

        // Broadcast group: posts, group chat, and meetings shared between a mentor and their
        // accepted mentees.
        mentors.MapGet("/{id:guid}/mentees", ListMentees).RequireAuthorization();
        mentors.MapGet("/{id:guid}/posts", ListPosts).RequireAuthorization();
        mentors.MapPost("/{id:guid}/posts", CreatePost).RequireAuthorization(MiragePolicy.Mentor);
        mentors.MapGet("/{id:guid}/group-messages", ListGroupMessages).RequireAuthorization();
        mentors.MapPost("/{id:guid}/group-messages", SendGroupMessage).RequireAuthorization();
        mentors.MapGet("/{id:guid}/meetings", ListMeetings).RequireAuthorization();
        mentors.MapPost("/{id:guid}/meetings", ScheduleMeeting).RequireAuthorization(MiragePolicy.Mentor);
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

    private static async Task<IResult> ListMentorMessages(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsMentorRequestPartyAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var messages = await db.MentorMessages.AsNoTracking()
            .Where(x => x.MentorRequestId == id)
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

    private static async Task<IResult> ListMentees(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var isMentor = await db.Mentors.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        var allowMenteesToSeeEachOther = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == id).Select(x => x.AllowMenteesToSeeEachOther).SingleAsync(cancellationToken);

        var mentees = await db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == id && x.Status == MentorRequestStatus.Accepted)
            .Join(db.Profiles.AsNoTracking(), r => r.MenteeUserId, p => p.UserId, (r, p) => new
            {
                r.Id,
                r.MenteeUserId,
                p.DisplayName,
                p.AvatarUrl,
                AcceptedAt = r.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var result = mentees.Select(x => isMentor || x.MenteeUserId == userId || allowMenteesToSeeEachOther
            ? new MentorMenteeResponse(x.Id, x.MenteeUserId, x.DisplayName, x.AvatarUrl, x.AcceptedAt)
            : new MentorMenteeResponse(x.Id, x.MenteeUserId, "Fellow mentee", null, x.AcceptedAt));

        return ApiResults.Ok(context, result, "Mentees retrieved successfully.");
    }

    private static async Task<IResult> ListPosts(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var posts = await db.MentorPosts.AsNoTracking()
            .Where(x => x.MentorProfileId == id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MentorPostResponse(x.Id, x.MentorProfileId, x.Content, x.ImageUrl, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, posts, "Posts retrieved successfully.");
    }

    private static async Task<IResult> CreatePost(Guid id, CreateMentorPostRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var isMentor = await db.Mentors.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (!isMentor) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Post content is required."));

        var post = new MentorPost(id, request.Content, request.ImageUrl);
        db.MentorPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/mentors/{id}/posts/{post.Id}", new { post.Id }, "Post published successfully.");
    }

    private static async Task<IResult> ListGroupMessages(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.UserId, x.AllowMenteesToSeeEachOther })
            .SingleAsync(cancellationToken);
        var isMentor = mentor.UserId == userId;

        var messages = await db.MentorGroupMessages.AsNoTracking()
            .Where(x => x.MentorProfileId == id)
            .OrderBy(x => x.CreatedAt)
            .Join(db.Profiles.AsNoTracking(), m => m.SenderId, p => p.UserId, (m, p) => new MentorGroupMessageResponse(
                m.Id, m.MentorProfileId, m.SenderId, p.DisplayName, m.Content, m.Type, m.AttachmentUrl, m.CreatedAt))
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
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Message content is required."));

        var message = new MentorGroupMessage(id, userId, request.Content, request.Type, request.AttachmentUrl);
        db.MentorGroupMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await hub.Clients.Group($"mentorgroup:{id}").SendAsync("ReceiveMentorGroupMessage", new
        {
            message.Id,
            MentorProfileId = id,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        }, cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentors/{id}/group-messages/{message.Id}",
            new { message.Id }, "Message sent successfully.");
    }

    private static async Task<IResult> ListMeetings(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var meetings = await db.MentorMeetings.AsNoTracking()
            .Where(x => x.MentorProfileId == id)
            .OrderBy(x => x.ScheduledAt)
            .Select(x => new MentorMeetingResponse(x.Id, x.MentorProfileId, x.ScheduledByUserId, x.Title,
                x.MeetingLink, x.ScheduledAt, x.DurationMinutes))
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

        var meeting = new MentorMeeting(id, userId, request.Title, request.MeetingLink, request.ScheduledAt, request.DurationMinutes);
        db.MentorMeetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);

        var menteeIds = await db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == id && x.Status == MentorRequestStatus.Accepted)
            .Select(x => x.MenteeUserId)
            .ToListAsync(cancellationToken);
        foreach (var menteeId in menteeIds)
            await notifications.NotifyAsync(menteeId, NotificationType.SessionBooked, "New meeting scheduled",
                $"{request.Title} was scheduled for {request.ScheduledAt:MMM d, h:mm tt}.", meeting.Id, "MentorMeeting",
                cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentors/{id}/meetings/{meeting.Id}", new { meeting.Id },
            "Meeting scheduled successfully.");
    }

    private static async Task<IResult> ListMentors(HttpContext context, IMirageDbContext db,
        string? denomination, string? areaOfGuidance, bool freeOnly = false,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var currentUserId = context.User.TryGetUserId();
        var query = db.Mentors.AsNoTracking().Where(x => x.IsApproved);
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
            x.Languages
        });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Mentors retrieved successfully.");
    }

    private static async Task<IResult> GetMentor(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var currentUserId = context.User.TryGetUserId();
        var isAcceptedMentee = currentUserId is not null && await db.MentorRequests.AsNoTracking()
            .AnyAsync(x => x.MentorProfileId == id && x.MenteeUserId == currentUserId
                && x.Status == MentorRequestStatus.Accepted, cancellationToken);

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
                PhoneNumber = isAcceptedMentee ? x.PhoneNumber : null
            })
            .SingleOrDefaultAsync(cancellationToken);
        return mentor is null
            ? EndpointHelpers.NotFound(context, "Mentor was not found.")
            : ApiResults.Ok(context, mentor, "Mentor retrieved successfully.");
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
                x.AreasOfGuidance, x.Languages, x.PhoneNumber, x.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return profile is null
            ? EndpointHelpers.NotFound(context, "Mentor profile was not found.")
            : ApiResults.Ok(context, profile, "Mentor profile retrieved successfully.");
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
            .Select(x => new { x.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (mentor is null)
            return EndpointHelpers.NotFound(context, "Approved mentor was not found.");

        if (await db.MentorRequests.AnyAsync(
                x => x.MentorProfileId == mentorId && x.MenteeUserId == userId &&
                     x.Status == MentorRequestStatus.Pending, cancellationToken))
            return EndpointHelpers.Conflict(context, "You already have a pending request to this mentor.");

        var mentorRequest = new MentorRequest(mentorId, userId, request.Message);
        db.MentorRequests.Add(mentorRequest);
        await db.SaveChangesAsync(cancellationToken);

        var menteeName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(mentor.UserId, NotificationType.MentorRequestReceived,
            "New mentorship request", $"{menteeName} requested your mentorship.",
            mentorRequest.Id, "MentorRequest", cancellationToken);

        return ApiResults.Created(context, $"/api/v1/mentorship/requests/{mentorRequest.Id}",
            new { mentorRequest.Id, mentorRequest.Status }, "Mentor request sent successfully.");
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
        MentorRequestStatus? status, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
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

        var result = query
            .Join(db.Profiles.AsNoTracking(), r => r.MenteeUserId, p => p.UserId, (r, p) => new
            {
                r.Id,
                r.MenteeUserId,
                MenteeName = p.DisplayName,
                MenteeAvatarUrl = p.AvatarUrl,
                r.Message,
                r.Status,
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
                x.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Mentor request was not found.");

        var mentee = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == request.MenteeUserId)
            .Select(x => new { x.DisplayName, x.AvatarUrl })
            .SingleOrDefaultAsync(cancellationToken);

        var response = new MentorRequestDetailResponse(request.Id, request.MentorProfileId, request.MentorUserId,
            request.MentorName, request.MentorAvatarUrl, request.MenteeUserId, mentee?.DisplayName ?? "Mentee",
            mentee?.AvatarUrl, request.Message, request.Status, request.CreatedAt, request.MentorPhoneNumber);
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
        if (request.Status != MentorRequestStatus.Pending)
            return EndpointHelpers.Conflict(context, "Only pending requests can be withdrawn.");
        request.Withdraw();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { request.Id, request.Status }, "Mentor request withdrawn.");
    }
}
