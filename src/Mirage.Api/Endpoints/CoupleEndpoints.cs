using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class CoupleEndpoints
{
    public static RouteGroupBuilder MapCoupleEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/couples").WithTags("Couples").RequireAuthorization();
        group.MapGet("/mine", GetMine);
        group.MapPost("/invite", Invite);
        group.MapPatch("/{id:guid}/approve", Approve);
        group.MapPatch("/{id:guid}/decline", Decline);
        return group;
    }

    private static async Task<IResult> GetMine(HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var couples = await db.Couples.AsNoTracking()
            .Where(x => x.User1Id == userId || x.User2Id == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { Couple = x, OtherUserId = x.User1Id == userId ? x.User2Id : x.User1Id })
            .ToListAsync(cancellationToken);

        var otherIds = couples.Select(x => x.OtherUserId).Distinct().ToList();
        var names = await db.Profiles.AsNoTracking()
            .Where(p => otherIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.DisplayName, cancellationToken);
        var badges = await db.GetOrgBadgesAsync(otherIds, cancellationToken);

        var response = couples.Select(x => new CoupleResponse(
            x.Couple.Id, x.OtherUserId, names.GetValueOrDefault(x.OtherUserId, "Unknown"),
            x.Couple.RequestedByUserId, x.Couple.Status, x.Couple.CreatedAt,
            badges.GetValueOrDefault(x.OtherUserId)?.LogoUrl, badges.GetValueOrDefault(x.OtherUserId)?.OrganisationName)).ToList();
        return ApiResults.Ok(context, response, "Couple records retrieved successfully.");
    }

    private static async Task<IResult> Invite(InviteCoupleRequest request, HttpContext context, MirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PartnerEmail))
            return EndpointHelpers.ValidationProblem(context, ("partnerEmail", "Partner email is required."));

        var userId = context.User.GetUserId();
        var normalizedEmail = request.PartnerEmail.Trim().ToLowerInvariant();
        var partner = await db.Profiles.AsNoTracking()
            .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.UserId, u.Email })
            .SingleOrDefaultAsync(x => x.Email != null && x.Email.ToLower() == normalizedEmail, cancellationToken);
        if (partner is null) return EndpointHelpers.NotFound(context, "No account found with that email address.");
        if (partner.UserId == userId)
            return EndpointHelpers.ValidationProblem(context, ("partnerEmail", "You cannot link yourself as your own spouse."));

        var user1Id = userId.CompareTo(partner.UserId) < 0 ? userId : partner.UserId;
        var user2Id = userId.CompareTo(partner.UserId) < 0 ? partner.UserId : userId;
        if (await db.Couples.AnyAsync(x => x.User1Id == user1Id && x.User2Id == user2Id
                && x.Status != CoupleStatus.Declined, cancellationToken))
            return EndpointHelpers.Conflict(context, "A couple invitation already exists between you and this person.");

        var couple = new Couple(userId, partner.UserId);
        db.Couples.Add(couple);
        await db.SaveChangesAsync(cancellationToken);

        var requesterName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(partner.UserId, NotificationType.NewMatch, "Spouse link request",
            $"{requesterName} wants to link with you as a married couple.", couple.Id, "Couple", cancellationToken);

        return ApiResults.Created(context, $"/api/v1/couples/{couple.Id}", new { couple.Id, couple.Status },
            "Couple invitation sent successfully.");
    }

    private static async Task<IResult> Approve(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var couple = await db.Couples.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (couple is null) return EndpointHelpers.NotFound(context, "Couple invitation was not found.");

        try { couple.Approve(userId); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }

        var profile1 = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == couple.User1Id, cancellationToken);
        var profile2 = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == couple.User2Id, cancellationToken);
        profile1?.MarkMarried();
        profile2?.MarkMarried();

        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.User1Id == couple.User1Id && x.User2Id == couple.User2Id, cancellationToken);
        if (match is null)
        {
            match = new Match(couple.User1Id, couple.User2Id);
            db.Matches.Add(match);
        }
        try { match.OpenForCouple(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }

        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(couple.RequestedByUserId, NotificationType.NewMatch, "Couple link approved",
            "Your spouse approved your couple link. You're now shown as a married couple.",
            couple.Id, "Couple", cancellationToken);

        return ApiResults.Ok(context, new { couple.Id, couple.Status }, "Couple link approved successfully.");
    }

    private static async Task<IResult> Decline(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var couple = await db.Couples.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (couple is null) return EndpointHelpers.NotFound(context, "Couple invitation was not found.");
        try { couple.Decline(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { couple.Id, couple.Status }, "Couple invitation declined.");
    }
}
