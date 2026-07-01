using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
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

        var requests = api.MapGroup("/mentorship/requests").WithTags("Mentorship").RequireAuthorization();
        requests.MapGet("/mine", ListMyRequests);
        requests.MapGet("/incoming", ListIncomingRequests);
        requests.MapPost("/{mentorId:guid}", SendRequest);
        requests.MapPatch("/{id:guid}/accept", AcceptRequest);
        requests.MapPatch("/{id:guid}/decline", DeclineRequest);
        requests.MapDelete("/{id:guid}", WithdrawRequest);
        return api;
    }

    private static async Task<IResult> ListMentors(HttpContext context, IMirageDbContext db,
        string? denomination, string? areaOfGuidance, bool freeOnly = false,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = db.Mentors.AsNoTracking().Where(x => x.IsApproved);
        if (freeOnly) query = query.Where(x => x.AcceptsFreeSessions);
        if (!string.IsNullOrWhiteSpace(denomination))
            query = query.Where(x => EF.Functions.ILike(x.UserProfile.Denomination, $"%{denomination.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(areaOfGuidance))
            query = query.Where(x => x.AreasOfGuidance.Any(a => EF.Functions.ILike(a, $"%{areaOfGuidance.Trim()}%")));

        var result = query.OrderByDescending(x => x.YearsMarried).Select(x => new
        {
            x.Id,
            DisplayName = x.IsAnonymous ? MaskName(x.UserProfile.DisplayName) : x.UserProfile.DisplayName,
            x.UserProfile.Denomination,
            x.UserProfile.City,
            x.YearsMarried,
            x.IsAnonymous,
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
        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == id && x.IsApproved)
            .Select(x => new
            {
                x.Id,
                DisplayName = x.IsAnonymous ? MaskName(x.UserProfile.DisplayName) : x.UserProfile.DisplayName,
                x.UserProfile.Denomination,
                x.UserProfile.City,
                x.YearsMarried,
                Testimony = x.IsAnonymous ? null : x.Testimony,
                x.IsAnonymous,
                x.AcceptsFreeSessions,
                x.AreasOfGuidance,
                x.Languages
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
                x.IsApproved, x.IsAnonymous, x.AcceptsFreeSessions,
                x.AreasOfGuidance, x.Languages, x.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return profile is null
            ? EndpointHelpers.NotFound(context, "Mentor profile was not found.")
            : ApiResults.Ok(context, profile, "Mentor profile retrieved successfully.");
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
        profile.UpdateProfile(request.YearsMarried, request.Testimony, request.AreasOfGuidance, request.Languages, request.AcceptsFreeSessions);
        profile.ToggleAnonymity(request.IsAnonymous);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.Id }, "Mentor profile updated successfully.");
    }

    private static async Task<IResult> SendRequest(Guid mentorId, RequestMentorRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return EndpointHelpers.ValidationProblem(context, ("message", "A message is required."));

        var userId = context.User.GetUserId();
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
                MentorName = x.Mentor.IsAnonymous ? MaskName(x.Mentor.UserProfile.DisplayName) : x.Mentor.UserProfile.DisplayName,
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

        var result = query.OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.MenteeUserId,
                MenteeName = x.Mentor.UserProfile.DisplayName,
                x.Message,
                x.Status,
                x.CreatedAt
            });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Incoming mentor requests retrieved successfully.");
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

    private static string MaskName(string name) => string.Join(' ',
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length <= 2 ? $"{part[0]}*" : $"{part[0]}***{part[^1]}"));
}
