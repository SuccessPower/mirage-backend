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
        group.MapGet("/discover", DiscoverCouples);
        group.MapPost("/invite", Invite);
        group.MapPatch("/{id:guid}/approve", Approve);
        group.MapPatch("/{id:guid}/decline", Decline);
        group.MapPost("/{id:guid}/befriend", BefriendCouple);
        return group;
    }

    // Couples are only visible to other married members — the couple feed replaces the singles
    // feed for anyone in an approved couple.
    private static async Task<Guid?> GetMyApprovedCoupleIdAsync(Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        await db.Couples.AsNoTracking()
            .Where(c => c.Status == CoupleStatus.Approved && (c.User1Id == userId || c.User2Id == userId))
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<IResult> DiscoverCouples(HttpContext context, MirageDbContext db,
        string? search = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var myCoupleId = await GetMyApprovedCoupleIdAsync(userId, db, cancellationToken);
        if (myCoupleId is null)
            return EndpointHelpers.Forbidden(context, "Only married members can view and befriend couples.");

        var myCity = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.City).SingleOrDefaultAsync(cancellationToken);

        var activeCompleteProfiles = db.Profiles.AsNoTracking()
            .Where(p => p.IsProfileComplete && db.Users.Any(u => u.Id == p.UserId && u.IsActive));

        var query = db.Couples.AsNoTracking()
            .Where(c => c.Status == CoupleStatus.Approved && c.Id != myCoupleId
                && activeCompleteProfiles.Any(p => p.UserId == c.User1Id)
                && activeCompleteProfiles.Any(p => p.UserId == c.User2Id));

        // Same one-box search as the singles feed, but a couple matches when either
        // partner's name, city, denomination, or occupation matches.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(c => db.Profiles.Any(p =>
                (p.UserId == c.User1Id || p.UserId == c.User2Id)
                && (EF.Functions.ILike(p.DisplayName, term)
                    || EF.Functions.ILike(p.City, term)
                    || EF.Functions.ILike(p.Denomination, term)
                    || (p.Occupation != null && EF.Functions.ILike(p.Occupation, term)))));
        }

        var pagedCouples = await query
            .OrderByDescending(c => myCity != null && db.Profiles.Any(p =>
                (p.UserId == c.User1Id || p.UserId == c.User2Id) && p.City == myCity))
            .ThenByDescending(c => db.Profiles.Any(p =>
                (p.UserId == c.User1Id || p.UserId == c.User2Id) && p.IsVerified))
            .ThenByDescending(c => c.ReviewedAt)
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        var partnerIds = pagedCouples.Items
            .SelectMany(c => new[] { c.User1Id, c.User2Id }).Distinct().ToArray();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => partnerIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);
        var badges = await db.GetOrgBadgesAsync(partnerIds, cancellationToken);
        var friendCoupleIds = await db.CoupleFriendships.AsNoTracking()
            .Where(f => f.FriendUserId == userId && f.Status == CoupleFriendshipStatus.Active)
            .Select(f => f.CoupleId)
            .ToListAsync(cancellationToken);

        CouplePartnerSummary ToSummary(Guid partnerId)
        {
            var profile = profiles[partnerId];
            var badge = badges.GetValueOrDefault(partnerId);
            return new CouplePartnerSummary(profile.UserId, profile.DisplayName,
                EndpointHelpers.Age(profile.DateOfBirth), profile.AvatarUrl, profile.Bio, profile.City,
                profile.Country, profile.Denomination, profile.IsVerified, badge?.LogoUrl, badge?.OrganisationName);
        }

        var response = new Mirage.Application.Common.PagedResult<CoupleCardResponse>(
            pagedCouples.Items.Select(c => new CoupleCardResponse(
                c.Id, ToSummary(c.User1Id), ToSummary(c.User2Id),
                friendCoupleIds.Contains(c.Id), c.ReviewedAt)).ToList(),
            pagedCouples.Page, pagedCouples.PageSize, pagedCouples.TotalCount);
        return ApiResults.Ok(context, response, "Couples retrieved successfully.");
    }

    private static async Task<IResult> BefriendCouple(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var myCoupleId = await GetMyApprovedCoupleIdAsync(userId, db, cancellationToken);
        if (myCoupleId is null)
            return EndpointHelpers.Forbidden(context, "Only married members can view and befriend couples.");

        var couple = await db.Couples.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.Status == CoupleStatus.Approved, cancellationToken);
        if (couple is null) return EndpointHelpers.NotFound(context, "Couple was not found.");
        if (couple.Id == myCoupleId || couple.User1Id == userId || couple.User2Id == userId)
            return EndpointHelpers.ValidationProblem(context, ("id", "You cannot befriend your own couple."));

        var friendship = await db.CoupleFriendships.SingleOrDefaultAsync(
            f => f.CoupleId == id && f.FriendUserId == userId, cancellationToken);
        if (friendship is null)
        {
            friendship = new CoupleFriendship(id, userId);
            db.CoupleFriendships.Add(friendship);
        }
        else if (friendship.Status == CoupleFriendshipStatus.Active)
        {
            return EndpointHelpers.Conflict(context, "You are already friends with this couple.");
        }
        else
        {
            friendship.Reactivate();
        }
        await db.SaveChangesAsync(cancellationToken);

        var myName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        foreach (var partnerId in new[] { couple.User1Id, couple.User2Id })
        {
            await notifications.NotifyAsync(partnerId, NotificationType.CoupleFriendshipCreated,
                "New couple friend", $"{myName} is now friends with you as a couple. Say hello in your shared conversation.",
                friendship.Id, "CoupleFriendship", cancellationToken);
        }

        return ApiResults.Created(context, $"/api/v1/couple-friendships/{friendship.Id}",
            new { friendship.Id, friendship.CoupleId, friendship.Status },
            "You are now friends with this couple.");
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

        // Each spouse joins the Married community of their own church (they may attend different
        // ones) — gated on the mutual Couple approval above, not a self-reported RelationshipStatus.
        foreach (var spouseId in new[] { couple.User1Id, couple.User2Id })
        {
            var organisationId = await db.OrganisationMembers.AsNoTracking()
                .Where(x => x.UserId == spouseId && x.Status != OrganisationMemberStatus.Removed &&
                            x.Status != OrganisationMemberStatus.Rejected)
                .Select(x => (Guid?)x.OrganisationId)
                .FirstOrDefaultAsync(cancellationToken);
            if (organisationId.HasValue)
                await ChurchCommunityService.JoinChurchCommunityAsync(db, organisationId.Value,
                    Community.ChurchMarriedCategory, spouseId, cancellationToken);
        }
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
