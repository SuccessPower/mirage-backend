using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class ProfessionalInviteEndpoints
{
    public static RouteGroupBuilder MapProfessionalInviteEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/professional-invites").WithTags("Professional invitations").RequireAuthorization();
        group.MapGet("/me", GetMine);
        group.MapPost("/redeem", Redeem);
        group.MapGet("/requests", ListRequests);
        group.MapPatch("/requests/{id:guid}/accept", Accept);
        group.MapPatch("/requests/{id:guid}/decline", Decline);
        return api;
    }

    private static async Task<IResult> GetMine(HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var name = await db.Profiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.DisplayName).SingleAsync(ct);
        var mentor = await db.Mentors.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var counsellor = await db.Counsellors.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (mentor is null && counsellor is null) return EndpointHelpers.Forbidden(context);

        if (mentor is not null && mentor.InviteCode is null)
            mentor.SetInviteCode(await NewCode(name, db, ct));
        if (counsellor is not null && counsellor.InviteCode is null)
            counsellor.SetInviteCode(await NewCode(name, db, ct));
        await db.SaveChangesAsync(ct);

        return ApiResults.Ok(context, new
        {
            mentorCode = mentor?.InviteCode,
            counsellorCode = counsellor?.InviteCode,
            mentorLink = mentor is null ? null : $"/join?invite={mentor.InviteCode}",
            counsellorLink = counsellor is null ? null : $"/join?invite={counsellor.InviteCode}"
        }, "Invitation details retrieved successfully.");
    }

    /// <summary>
    /// Turns a mentor's or counsellor's invite code into a pending request against them. The
    /// professional is notified here: previously the row was written silently, so someone who
    /// invited a friend was never told the friend had arrived, and the request sat unanswered in
    /// a queue nobody knew had anything in it.
    /// </summary>
    internal static async Task<bool> RedeemCode(Guid memberUserId, string? rawCode, IMirageDbContext db,
        NotificationService notifications, CancellationToken ct)
    {
        var code = rawCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return true;
        var mentor = await db.Mentors.AsNoTracking().SingleOrDefaultAsync(x => x.InviteCode == code, ct);
        var counsellor = mentor is null
            ? await db.Counsellors.AsNoTracking().SingleOrDefaultAsync(x => x.InviteCode == code, ct)
            : null;
        var professionalUserId = mentor?.UserId ?? counsellor?.UserId;
        if (professionalUserId is null || professionalUserId == memberUserId) return false;

        Guid? referenceId = null;
        string referenceType;
        string actionUrl;

        if (mentor is not null)
        {
            referenceType = "MentorRequest";
            actionUrl = "/practice/mentorship";
            // An invited mentee who was declined or withdrew earlier already owns the single row
            // this pair is allowed by the unique index, so reopen it rather than inserting a
            // duplicate that SaveChanges would reject.
            var existing = await db.MentorRequests
                .SingleOrDefaultAsync(x => x.MentorProfileId == mentor.Id && x.MenteeUserId == memberUserId, ct);
            if (existing is null)
            {
                var created = new MentorRequest(mentor.Id, memberUserId, "Requested using your invite code.");
                db.MentorRequests.Add(created);
                referenceId = created.Id;
            }
            else if (existing.Status is MentorRequestStatus.Declined or MentorRequestStatus.Withdrawn)
            {
                existing.Reopen("Requested using your invite code.", existing.Tier);
                referenceId = existing.Id;
            }
            else
            {
                // Already pending or accepted — the code is valid, but there is nothing new to
                // tell the mentor about.
                return true;
            }
        }
        else
        {
            referenceType = "ProfessionalConnection";
            actionUrl = "/practice/counselling";
            if (await db.ProfessionalConnections.AnyAsync(x => x.ProfessionalUserId == professionalUserId &&
                    x.MemberUserId == memberUserId && x.Role == ProfessionalRole.Counsellor, ct))
                return true;
            var connection = new ProfessionalConnection(professionalUserId.Value, memberUserId,
                ProfessionalRole.Counsellor);
            db.ProfessionalConnections.Add(connection);
            referenceId = connection.Id;
        }

        await db.SaveChangesAsync(ct);

        var memberName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == memberUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct)
            ?? "Someone you invited";
        await notifications.NotifyAsync(professionalUserId.Value, NotificationType.ProfessionalInviteRedeemed,
            "Your invite was used",
            $"{memberName} joined with your invite code and is waiting for you to accept them.",
            referenceId, referenceType, ct, actionUrl);
        return true;
    }

    internal static async Task<bool> IsValidCode(string? rawCode, IMirageDbContext db, CancellationToken ct)
    {
        var code = rawCode?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(code) || await db.Mentors.AnyAsync(x => x.InviteCode == code, ct) ||
            await db.Counsellors.AnyAsync(x => x.InviteCode == code, ct);
    }

    private static async Task<IResult> Redeem(RedeemProfessionalInviteRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        if (!await RedeemCode(userId, request.Code, db, notifications, ct))
            return EndpointHelpers.ValidationProblem(context, ("code", "Invite code is invalid."));
        return ApiResults.Ok(context, new { status = "Pending" }, "Request sent for professional approval.");
    }

    private static async Task<IResult> ListRequests(HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var requests = await db.ProfessionalConnections.AsNoTracking()
            .Where(x => x.ProfessionalUserId == userId)
            // Left join, not inner: a member whose profile row is missing or not yet created must
            // still appear in the professional's queue rather than vanishing from it.
            .Select(x => new
            {
                x.Id,
                x.MemberUserId,
                DisplayName = db.Profiles.Where(p => p.UserId == x.MemberUserId)
                    .Select(p => p.DisplayName).FirstOrDefault(),
                AvatarUrl = db.Profiles.Where(p => p.UserId == x.MemberUserId)
                    .Select(p => p.AvatarUrl).FirstOrDefault(),
                x.Role,
                x.Status,
                x.CreatedAt,
            })
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return ApiResults.Ok(context, requests, "Connection requests retrieved successfully.");
    }

    private static async Task<IResult> Accept(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken ct) =>
        await Decide(id, true, context, db, notifications, ct);

    private static async Task<IResult> Decline(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken ct) =>
        await Decide(id, false, context, db, notifications, ct);

    private static async Task<IResult> Decide(Guid id, bool accepted, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken ct)
    {
        var userId = context.User.GetUserId();
        var request = await db.ProfessionalConnections.SingleOrDefaultAsync(x => x.Id == id && x.ProfessionalUserId == userId, ct);
        if (request is null) return EndpointHelpers.NotFound(context, "Connection request was not found.");
        if (request.Status != ProfessionalConnectionStatus.Pending)
            return EndpointHelpers.Conflict(context, "This request is no longer pending.");
        if (accepted) request.Accept(); else request.Decline();
        await db.SaveChangesAsync(ct);
        await notifications.NotifyAsync(request.MemberUserId, NotificationType.ProfessionalConnectionRequest,
            accepted ? "Connection approved" : "Connection declined",
            accepted ? "Your counsellor approved your connection request." : "Your counsellor declined your connection request.",
            request.Id, "ProfessionalConnection", ct, "/counselling");
        return ApiResults.Ok(context, new { request.Id, request.Status }, $"Request {(accepted ? "accepted" : "declined")}.");
    }

    private static async Task<string> NewCode(string name, IMirageDbContext db, CancellationToken ct)
    {
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2)
            .Select(x => char.ToUpperInvariant(x[0])));
        if (initials.Length == 0) initials = "MH";
        for (var i = 0; i < 20; i++)
        {
            var code = $"{initials}-{Random.Shared.Next(1000, 10000)}";
            if (!await db.Mentors.AnyAsync(x => x.InviteCode == code, ct) &&
                !await db.Counsellors.AnyAsync(x => x.InviteCode == code, ct)) return code;
        }
        return $"{initials}-{Guid.NewGuid():N}"[..Math.Min(initials.Length + 7, 16)].ToUpperInvariant();
    }
}
