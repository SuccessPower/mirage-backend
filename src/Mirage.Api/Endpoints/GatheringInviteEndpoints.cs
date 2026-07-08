using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class GatheringInviteEndpoints
{
    public static RouteGroupBuilder MapGatheringInviteEndpoints(this RouteGroupBuilder api)
    {
        var invites = api.MapGroup("/invites").WithTags("Gathering Invites").RequireAuthorization();
        invites.MapGet("/pending", ListPending);
        invites.MapPost("/{id:guid}/accept", Accept);
        invites.MapPost("/{id:guid}/decline", Decline);
        return api;
    }

    private static async Task<IResult> ListPending(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var pending = await db.GatheringInvites.AsNoTracking()
            .Where(x => x.InviteeUserId == userId && x.Status == GatheringInviteStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return ApiResults.Ok(context, Array.Empty<GatheringInviteResponse>(), "Pending invites retrieved successfully.");

        var communityIds = pending.Where(x => x.Kind == GatheringInviteKind.Community).Select(x => x.TargetId).ToArray();
        var dateRequestIds = pending.Where(x => x.Kind == GatheringInviteKind.DateRequest).Select(x => x.TargetId).ToArray();
        var inviterIds = pending.Select(x => x.InviterUserId).Distinct().ToArray();

        var communityTitles = await db.Communities.AsNoTracking()
            .Where(x => communityIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var dateRequestTitles = await db.DateRequests.AsNoTracking()
            .Where(x => dateRequestIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Activity, cancellationToken);
        var inviterProfiles = await db.Profiles.AsNoTracking()
            .Where(x => inviterIds.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, x => new { x.DisplayName, x.AvatarUrl }, cancellationToken);

        var response = pending.Select(x => new GatheringInviteResponse(
            x.Id,
            x.Kind,
            x.TargetId,
            x.Kind == GatheringInviteKind.Community
                ? communityTitles.GetValueOrDefault(x.TargetId, "Community")
                : dateRequestTitles.GetValueOrDefault(x.TargetId, "Gathering"),
            x.InviterUserId,
            inviterProfiles.TryGetValue(x.InviterUserId, out var inviter) ? inviter.DisplayName : "Member",
            inviterProfiles.TryGetValue(x.InviterUserId, out var inviterProfile) ? inviterProfile.AvatarUrl : null,
            x.Status,
            x.CreatedAt)).ToList();

        return ApiResults.Ok(context, response, "Pending invites retrieved successfully.");
    }

    private static async Task<IResult> Accept(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var invite = await db.GatheringInvites.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (invite is null) return EndpointHelpers.NotFound(context, "Invite was not found.");
        if (invite.InviteeUserId != userId) return EndpointHelpers.Forbidden(context);
        if (invite.Status != GatheringInviteStatus.Pending)
            return EndpointHelpers.Conflict(context, "This invite has already been responded to.");

        if (invite.Kind == GatheringInviteKind.Community)
        {
            var communityExists = await db.Communities.AsNoTracking()
                .AnyAsync(x => x.Id == invite.TargetId && x.Status == CommunityStatus.Active, cancellationToken);
            if (!communityExists) return EndpointHelpers.NotFound(context, "Community was not found.");

            var member = await db.CommunityMembers.SingleOrDefaultAsync(
                x => x.CommunityId == invite.TargetId && x.UserId == userId, cancellationToken);
            if (member is null)
                db.CommunityMembers.Add(new CommunityMember(invite.TargetId, userId));
            else if (member.LeftAt is not null)
                member.Rejoin();
        }
        else
        {
            var dateRequest = await db.DateRequests.Include(x => x.Acceptances)
                .SingleOrDefaultAsync(x => x.Id == invite.TargetId, cancellationToken);
            if (dateRequest is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
            if (dateRequest.Status != DateRequestStatus.Open)
                return EndpointHelpers.Conflict(context, "This gathering is no longer open.");

            var acceptance = dateRequest.Acceptances.SingleOrDefault(x => x.AcceptorUserId == userId);
            if (acceptance is null)
            {
                acceptance = new DateRequestAcceptance(invite.TargetId, userId);
                dateRequest.Acceptances.Add(acceptance);
            }

            try
            {
                dateRequest.Select(userId);
            }
            catch (InvalidOperationException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
        }

        invite.Accept();
        await db.SaveChangesAsync(cancellationToken);

        var accepterName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(invite.InviterUserId, NotificationType.GatheringInviteAccepted,
            "Invite accepted", $"{accepterName ?? "Someone"} accepted your invite.", invite.Id, "GatheringInvite",
            cancellationToken);

        return ApiResults.Ok(context, new { invite.Id, invite.Kind, invite.TargetId }, "Invite accepted successfully.");
    }

    private static async Task<IResult> Decline(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var invite = await db.GatheringInvites.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (invite is null) return EndpointHelpers.NotFound(context, "Invite was not found.");
        if (invite.InviteeUserId != userId) return EndpointHelpers.Forbidden(context);
        if (invite.Status != GatheringInviteStatus.Pending)
            return EndpointHelpers.Conflict(context, "This invite has already been responded to.");

        invite.Decline();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(invite.InviterUserId, NotificationType.GatheringInviteDeclined,
            "Invite declined", "Your gathering invite was declined.", invite.Id, "GatheringInvite", cancellationToken);

        return ApiResults.Ok(context, new { invite.Id }, "Invite declined successfully.");
    }
}
