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

namespace Mirage.Api.Endpoints;

internal static class MatchingEndpoints
{
    public static RouteGroupBuilder MapMatchingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/matching").WithTags("Matching").RequireAuthorization();
        group.MapPost("/likes", Like);
        group.MapGet("/likes/mine", GetMyLikes);
        group.MapGet("/matches", GetMatches);
        group.MapGet("/matches/{id:guid}", GetMatch);
        group.MapDelete("/matches/{id:guid}", CloseMatch);
        group.MapPost("/matches/{id:guid}/block", BlockMatch);
        group.MapPost("/matches/{id:guid}/unblock", UnblockMatch);
        group.MapPost("/matches/{id:guid}/chat-request", RequestChat);
        group.MapPost("/matches/{id:guid}/chat-request/approve", ApproveChatRequest);

        group.MapGet("/conversations/slots", GetConversationSlots);

        // Chat — REST fallback alongside SignalR hub
        group.MapGet("/messages/unread", GetUnreadMessageCounts);
        group.MapGet("/matches/{id:guid}/messages", GetMessages);
        group.MapGet("/matches/{id:guid}/messages/legacy", GetLegacyMessages);
        group.MapPost("/matches/{id:guid}/messages", SendMessage);
        group.MapPatch("/matches/{id:guid}/messages/encrypt", EncryptExistingMessages);
        group.MapPatch("/matches/{id:guid}/messages/read", MarkRead);
        return api;
    }

    private static async Task<IResult> Like(LikeProfileRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, UserManager<ApplicationUser> userManager,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var sourceUserId = context.User.GetUserId();
        if (sourceUserId == request.TargetUserId)
            return EndpointHelpers.ValidationProblem(context, ("targetUserId", "A user cannot like themselves."));
        var emailForbidden = await EndpointHelpers.RequireEmailConfirmedAsync(context, sourceUserId, userManager,
            "Confirm your email address before liking or matching with other members.");
        if (emailForbidden is not null) return emailForbidden;
        var photoForbidden = await EndpointHelpers.RequirePhotoAsync(context, sourceUserId, db, cancellationToken,
            "Add a profile photo of your face before liking or matching with other members.");
        if (photoForbidden is not null) return photoForbidden;
        var profileStatuses = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == sourceUserId || x.UserId == request.TargetUserId)
            .Select(x => new { x.UserId, x.RelationshipStatus, x.IsVerified, x.Sex })
            .ToListAsync(cancellationToken);
        if (!profileStatuses.Any(x => x.UserId == request.TargetUserId))
            return EndpointHelpers.NotFound(context, "Target profile was not found.");
        var targetUser = await userManager.FindByIdAsync(request.TargetUserId.ToString());
        if (targetUser is null || !targetUser.IsActive)
            return EndpointHelpers.NotFound(context, "Target profile was not found.");
        if (profileStatuses.SingleOrDefault(x => x.UserId == sourceUserId)?.IsVerified != true)
            return EndpointHelpers.Forbidden(context, "Verify your profile before liking or matching with other members.");
        // Dating and marriage are opposite-sex only, mirroring the discovery feed's own filter
        // (ProfileEndpoints.Discover) — friendship has no gender restriction.
        if (request.Category is SectionCategory.Dating or SectionCategory.Marriage)
        {
            var sourceSex = profileStatuses.SingleOrDefault(x => x.UserId == sourceUserId)?.Sex;
            var targetSex = profileStatuses.SingleOrDefault(x => x.UserId == request.TargetUserId)?.Sex;
            if (sourceSex.HasValue && targetSex.HasValue && sourceSex == targetSex)
                return EndpointHelpers.Forbidden(context, "Dating and marriage connections are opposite-sex only.");
        }
        if (profileStatuses.Any(x => x.RelationshipStatus == RelationshipStatus.Married))
        {
            Match? coupleMatch;
            try
            {
                coupleMatch = await TryOpenApprovedCoupleMatchAsync(
                    sourceUserId, request.TargetUserId, db, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }

            if (coupleMatch is not null)
                return ApiResults.Ok(context,
                    new { isMatch = true, matchId = coupleMatch.Id, status = coupleMatch.Status.ToString() },
                    "Couple chat is ready.");

            // Married members can still become friends with each other through the Friendship
            // marital peer group — falls through to the normal match flow below, just under the
            // married-friendship conversation cap instead of the regular one.
            var bothMarried = profileStatuses.All(x => x.RelationshipStatus == RelationshipStatus.Married);
            if (request.Category != SectionCategory.Friendship || !bothMarried)
                return EndpointHelpers.Forbidden(context, "Married users can view and share profiles, but cannot engage in matching.");
        }
        var user1Id = sourceUserId.CompareTo(request.TargetUserId) < 0 ? sourceUserId : request.TargetUserId;
        var user2Id = sourceUserId.CompareTo(request.TargetUserId) < 0 ? request.TargetUserId : sourceUserId;
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.User1Id == user1Id && x.User2Id == user2Id, cancellationToken);

        if (match?.Status == MatchStatus.Blocked)
            return EndpointHelpers.Conflict(context, "This match is blocked.");

        // A repeat like is only a conflict while the pair still has a live match — a Closed
        // one is revived below, so ending a conversation is never a permanent dead end.
        var alreadyLiked = await db.Likes.AnyAsync(
            x => x.SourceUserId == sourceUserId && x.TargetUserId == request.TargetUserId, cancellationToken);
        if (alreadyLiked && (match is null || match.Status != MatchStatus.Closed))
            return EndpointHelpers.Conflict(context, "Like already recorded.");

        if (!alreadyLiked)
        {
            db.Likes.Add(new UserLike(sourceUserId, request.TargetUserId, request.Type));
            await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ProfileLiked,
                sourceUserId, request.TargetUserId, null, cancellationToken);
        }

        var sourceName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == sourceUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);

        var justMatched = false;
        if (match is null)
        {
            // First like between this pair immediately raises a visible chat request to the target,
            // instead of requiring a reciprocal like before either party sees anything.
            match = new Match(sourceUserId, request.TargetUserId);
            db.Matches.Add(match);
            match.RequestChat(sourceUserId);
            await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ChatRequested,
                sourceUserId, request.TargetUserId, match.Id, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await notifications.NotifyAsync(request.TargetUserId, NotificationType.ChatRequestReceived,
                "New chat request", $"{sourceName} liked your profile and wants to start chatting.",
                match.Id, "Match", cancellationToken, InboxUrl(configuration), "Review request");
        }
        else if (match.Status == MatchStatus.Closed)
        {
            // A previously-ended conversation restarts as a fresh chat request the other
            // party must approve again.
            match.ReopenRequest(sourceUserId);
            await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ChatRequested,
                sourceUserId, request.TargetUserId, match.Id, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await notifications.NotifyAsync(request.TargetUserId, NotificationType.ChatRequestReceived,
                "New chat request", $"{sourceName} wants to reconnect and start chatting again.",
                match.Id, "Match", cancellationToken, InboxUrl(configuration), "Review request");
        }
        else if (match.Status == MatchStatus.PendingRequest && match.ChatRequestedByUserId != sourceUserId)
        {
            // The other party already has a pending request out (from their own like) — liking them
            // back approves it immediately, mirroring "mutual like = instant match".
            var requesterId = match.ChatRequestedByUserId!.Value;
            match.ApproveChat(sourceUserId);
            justMatched = true;
            await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ChatApproved,
                sourceUserId, requesterId, match.Id, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await notifications.NotifyAsync(requesterId, NotificationType.ChatRequestApproved,
                "It's a match!", $"{sourceName} liked you back — you can start chatting now.",
                match.Id, "Match", cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return ApiResults.Ok(context,
            new { isMatch = match.Status == MatchStatus.Active, matchId = match.Id, status = match.Status.ToString() },
            justMatched ? "It's a match!"
                : match.Status == MatchStatus.Active ? "You are already connected."
                : "Like sent — chat request delivered.");
    }

    private static async Task<IResult> GetMyLikes(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        // Likes whose match has since been closed are omitted so the client treats those
        // people as fresh again — the pair can restart via a new like (see Like above).
        var targetUserIds = await db.Likes.AsNoTracking()
            .Where(x => x.SourceUserId == userId)
            .Where(x => !db.Matches.Any(m =>
                ((m.User1Id == userId && m.User2Id == x.TargetUserId)
                    || (m.User1Id == x.TargetUserId && m.User2Id == userId))
                && m.Status == MatchStatus.Closed))
            .Select(x => x.TargetUserId)
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, targetUserIds, "Your likes were retrieved successfully.");
    }

    private static async Task<IResult> GetMatches(HttpContext context, IMirageDbContext db,
        UserManager<ApplicationUser> userManager, PresenceTracker presence, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var matches = await db.Matches.AsNoTracking()
            .Where(x => x.User1Id == userId || x.User2Id == userId)
            .OrderByDescending(x => x.MatchedAt).ToListAsync(cancellationToken);
        var response = await ToMatchResponsesAsync(matches, userId, db, userManager, presence, cancellationToken);
        return ApiResults.Ok(context, response, "Matches retrieved successfully.");
    }

    private static async Task<IResult> GetMatch(Guid id, HttpContext context, IMirageDbContext db,
        UserManager<ApplicationUser> userManager, PresenceTracker presence, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        var response = (await ToMatchResponsesAsync([match], userId, db, userManager, presence, cancellationToken)).SingleOrDefault();
        return response is null
            ? EndpointHelpers.NotFound(context, "Match was not found.")
            : ApiResults.Ok(context, response, "Match retrieved successfully.");
    }

    // Matches carry only the two user ids — enrich with the other party's display name/avatar
    // so the client doesn't have to fan out N extra profile lookups per match list render.
    private static async Task<List<MatchResponse>> ToMatchResponsesAsync(List<Match> matches, Guid userId,
        IMirageDbContext db, UserManager<ApplicationUser> userManager, PresenceTracker presence,
        CancellationToken cancellationToken)
    {
        var otherIds = matches.Select(m => m.User1Id == userId ? m.User2Id : m.User1Id).Distinct().ToList();
        var approvedSpouseIds = await db.Couples.AsNoTracking()
            .Where(c => c.Status == CoupleStatus.Approved && (c.User1Id == userId || c.User2Id == userId))
            .Select(c => c.User1Id == userId ? c.User2Id : c.User1Id)
            .ToListAsync(cancellationToken);
        // LastSeenAt rides along with the active check so a match list can render "online" or
        // "last seen …" without a second round trip per row.
        var activeUsers = await userManager.Users.AsNoTracking()
            .Where(u => otherIds.Contains(u.Id) && u.IsActive)
            .Select(u => new { u.Id, u.LastSeenAt })
            .ToDictionaryAsync(u => u.Id, u => u.LastSeenAt, cancellationToken);
        var activeIds = activeUsers.Keys.ToList();
        // Married-married Friendship matches (see MatchingEndpoints.Like) legitimately pair two
        // married non-spouses, so the married-profile guard below must not hide those from a
        // married viewer — only from an unmarried one (who should never have such a match anyway).
        var iAmMarried = await db.Profiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.RelationshipStatus == RelationshipStatus.Married)
            .SingleOrDefaultAsync(cancellationToken);

        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => otherIds.Contains(p.UserId) && activeIds.Contains(p.UserId)
                && (p.RelationshipStatus != RelationshipStatus.Married || approvedSpouseIds.Contains(p.UserId) || iAmMarried))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);
        var badges = await db.GetOrgBadgesAsync(otherIds, cancellationToken);

        return matches.Select(m =>
        {
            var otherId = m.User1Id == userId ? m.User2Id : m.User1Id;
            profiles.TryGetValue(otherId, out var profile);
            if (profile is null) return null;
            var badge = badges.GetValueOrDefault(otherId);
            var isOnline = presence.IsOnline(otherId);
            return new MatchResponse(m.Id, otherId, profile?.DisplayName ?? "Unknown", profile?.AvatarUrl,
                profile?.IsVerified ?? false, profile?.RelationshipStatus, m.Status, m.ChatRequestedByUserId,
                m.MatchedAt, m.LastActivityAt, badge?.LogoUrl, badge?.OrganisationName, m.ClosedByUserId,
                isOnline, isOnline ? null : activeUsers.GetValueOrDefault(otherId), m.BlockedByUserId);
        }).Where(x => x is not null).Cast<MatchResponse>().ToList();
    }

    // Conversations are uncapped for every tier — limit is always null so the client's
    // "n of N conversations" banner never renders; used is kept for informational purposes.
    private static async Task<IResult> GetConversationSlots(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var tier = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.SubscriptionTier)
            .SingleOrDefaultAsync(cancellationToken);
        var (used, _) = await ConversationLimits.CountOpenAsync(userId, db, cancellationToken);
        return ApiResults.Ok(context, new { used, limit = (int?)null, tier = tier.ToString() },
            "Conversation slots retrieved successfully.");
    }

    private static async Task<IResult> CloseMatch(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        if (match.Status is MatchStatus.Closed or MatchStatus.Blocked)
            return EndpointHelpers.Conflict(context, "Match is already closed.");
        match.Close(userId);
        var otherUserId = match.User1Id == userId ? match.User2Id : match.User1Id;
        await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ConversationClosed,
            userId, otherUserId, match.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var enderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(otherUserId, NotificationType.ConversationEnded, "Conversation ended",
            $"{enderName} ended your conversation.", match.Id, "Match", cancellationToken);

        return ApiResults.Ok(context, new { match.Id, match.Status, match.ClosedByUserId }, "Match closed successfully.");
    }

    // Either party can send the first chat request; the other party then approves
    // (opens the thread) or declines by closing the match via the existing Close endpoint.
    private static async Task<IResult> RequestChat(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, UserManager<ApplicationUser> userManager, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var emailForbidden = await EndpointHelpers.RequireEmailConfirmedAsync(context, userId, userManager,
            "Confirm your email address before requesting to chat with other members.");
        if (emailForbidden is not null) return emailForbidden;
        var photoForbidden = await EndpointHelpers.RequirePhotoAsync(context, userId, db, cancellationToken,
            "Add a profile photo of your face before requesting to chat with other members.");
        if (photoForbidden is not null) return photoForbidden;
        if (!await IsProfileVerifiedAsync(userId, db, cancellationToken))
            return EndpointHelpers.Forbidden(context, "Verify your profile before requesting to chat with other members.");
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");

        var otherUserId = match.User1Id == userId ? match.User2Id : match.User1Id;
        if (match.Status != MatchStatus.Active &&
            await IsApprovedCoupleAsync(userId, otherUserId, db, cancellationToken))
        {
            try { match.OpenForCouple(); }
            catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }

            await db.SaveChangesAsync(cancellationToken);
            return ApiResults.Ok(context, new { match.Id, match.Status, match.ChatRequestedByUserId },
                "Couple chat is ready.");
        }

        try
        {
            match.RequestChat(userId);
        }
        catch (InvalidOperationException ex)
        {
            return EndpointHelpers.Conflict(context, ex.Message);
        }
        await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ChatRequested,
            userId, otherUserId, match.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var requesterName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(otherUserId, NotificationType.ChatRequestReceived, "New chat request",
            $"{requesterName} wants to start chatting with you.", match.Id, "Match", cancellationToken,
            InboxUrl(configuration), "Review request");

        return ApiResults.Ok(context, new { match.Id, match.Status, match.ChatRequestedByUserId },
            "Chat request sent successfully.");
    }

    private static async Task<IResult> ApproveChatRequest(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var emailForbidden = await EndpointHelpers.RequireEmailConfirmedAsync(context, userId, userManager,
            "Confirm your email address before approving chat requests.");
        if (emailForbidden is not null) return emailForbidden;
        var photoForbidden = await EndpointHelpers.RequirePhotoAsync(context, userId, db, cancellationToken,
            "Add a profile photo of your face before approving chat requests.");
        if (photoForbidden is not null) return photoForbidden;
        if (!await IsProfileVerifiedAsync(userId, db, cancellationToken))
            return EndpointHelpers.Forbidden(context, "Verify your profile before approving chat requests.");
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");

        try
        {
            match.ApproveChat(userId);
        }
        catch (InvalidOperationException ex)
        {
            return EndpointHelpers.Conflict(context, ex.Message);
        }
        await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ChatApproved,
            userId, match.ChatRequestedByUserId!.Value, match.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var approverName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(match.ChatRequestedByUserId!.Value, NotificationType.ChatRequestApproved,
            "Chat request approved", $"{approverName} approved your chat request. You can start chatting now.",
            match.Id, "Match", cancellationToken);

        return ApiResults.Ok(context, new { match.Id, match.Status }, "Chat request approved successfully.");
    }

    private static async Task<Match?> TryOpenApprovedCoupleMatchAsync(Guid userId, Guid partnerUserId,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (!await IsApprovedCoupleAsync(userId, partnerUserId, db, cancellationToken))
            return null;

        var user1Id = userId.CompareTo(partnerUserId) < 0 ? userId : partnerUserId;
        var user2Id = userId.CompareTo(partnerUserId) < 0 ? partnerUserId : userId;
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.User1Id == user1Id && x.User2Id == user2Id, cancellationToken);
        if (match is null)
        {
            match = new Match(user1Id, user2Id);
            db.Matches.Add(match);
        }

        match.OpenForCouple();
        await db.SaveChangesAsync(cancellationToken);
        return match;
    }

    private static Task<bool> IsApprovedCoupleAsync(Guid userId, Guid partnerUserId,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var user1Id = userId.CompareTo(partnerUserId) < 0 ? userId : partnerUserId;
        var user2Id = userId.CompareTo(partnerUserId) < 0 ? partnerUserId : userId;
        return db.Couples.AsNoTracking().AnyAsync(
            x => x.User1Id == user1Id && x.User2Id == user2Id && x.Status == CoupleStatus.Approved,
            cancellationToken);
    }

    private static Task<bool> IsProfileVerifiedAsync(Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        db.Profiles.AsNoTracking().AnyAsync(x => x.UserId == userId && x.IsVerified, cancellationToken);

    private static async Task<IResult> BlockMatch(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        if (match.Status == MatchStatus.Blocked)
            return EndpointHelpers.Conflict(context, "Match is already blocked.");
        match.Block(userId);
        var blockedOtherUserId = match.User1Id == userId ? match.User2Id : match.User1Id;
        await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.ConversationBlocked,
            userId, blockedOtherUserId, match.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { match.Id, match.Status, match.BlockedByUserId },
            "Match blocked successfully.");
    }

    private static async Task<IResult> UnblockMatch(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        try { match.Unblock(userId); }
        catch (InvalidOperationException exception) { return EndpointHelpers.Conflict(context, exception.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { match.Id, match.Status, match.ChatRequestedByUserId },
            "Match unblocked successfully.");
    }

    // The client's unread badge is otherwise fed only by live SignalR pushes, so it reset to
    // zero on every reload and never accounted for messages that landed while the tab was
    // closed. This is the equivalent of /notifications/unread-count for chat.
    private static async Task<IResult> GetUnreadMessageCounts(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var counts = await db.Messages.AsNoTracking()
            .Where(m => !m.IsRead && m.SenderId != userId
                && db.Matches.Any(x => x.Id == m.MatchId
                    && x.Status == MatchStatus.Active
                    && (x.User1Id == userId || x.User2Id == userId)))
            .GroupBy(m => m.MatchId)
            .Select(g => new { MatchId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return ApiResults.Ok(context, new
        {
            Total = counts.Sum(x => x.Count),
            Matches = counts
        }, "Unread message counts retrieved successfully.");
    }

    // Cursor-based pagination: pass the CreatedAt of the oldest message in the current
    // view as `before` to load earlier messages (standard chat scroll-back pattern).
    private static async Task<IResult> GetMessages(Guid id, HttpContext context, IMirageDbContext db,
        DateTimeOffset? before, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var userId = context.User.GetUserId();
        var inMatch = await db.Matches.AsNoTracking()
            .AnyAsync(x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (!inMatch) return EndpointHelpers.Forbidden(context);

        var query = db.Messages.AsNoTracking().Where(x => x.MatchId == id);
        if (before.HasValue) query = query.Where(x => x.CreatedAt < before.Value);

        var messages = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.SenderId,
                x.Content,
                x.Type,
                x.AttachmentUrl,
                x.Ciphertext,
                x.EncryptionNonce,
                x.ClientMessageId,
                x.EncryptionVersion,
                x.ReplyToMessageId,
                ReplyToSenderId = x.ReplyToMessage == null ? (Guid?)null : x.ReplyToMessage.SenderId,
                ReplyToContent = x.ReplyToMessage == null ? null : x.ReplyToMessage.Content,
                ReplyToType = x.ReplyToMessage == null ? (MessageType?)null : x.ReplyToMessage.Type,
                ReplyToAttachmentUrl = x.ReplyToMessage == null ? null : x.ReplyToMessage.AttachmentUrl,
                SentAt = x.CreatedAt,
                x.IsRead,
                x.ReadAt
            })
            .ToListAsync(cancellationToken);

        // Return in chronological order for the client to append
        return ApiResults.Ok(context, new
        {
            Messages = Enumerable.Reverse(messages),
            HasMore = messages.Count == pageSize
        }, "Messages retrieved successfully.");
    }

    private static async Task<IResult> GetLegacyMessages(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var inMatch = await db.Matches.AsNoTracking().AnyAsync(x => x.Id == id
            && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (!inMatch) return EndpointHelpers.Forbidden(context);

        var messages = await db.Messages.AsNoTracking()
            .Where(x => x.MatchId == id && x.EncryptionVersion == 0)
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .Select(x => new
            {
                x.Id, x.SenderId, x.Content, x.Type, x.AttachmentUrl,
                ReplyToSenderId = x.ReplyToMessage == null ? (Guid?)null : x.ReplyToMessage.SenderId,
                ReplyToContent = x.ReplyToMessage == null ? null : x.ReplyToMessage.Content,
                ReplyToType = x.ReplyToMessage == null ? (MessageType?)null : x.ReplyToMessage.Type,
                ReplyToAttachmentUrl = x.ReplyToMessage == null ? null : x.ReplyToMessage.AttachmentUrl
            })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, messages, "Legacy messages retrieved for encryption.");
    }

    // REST fallback for sending messages (e.g. image messages after a Cloudinary upload
    // completes) — mirrors ChatHub.SendMessage and broadcasts through the same hub group
    // so connected SignalR clients still receive it in real time.
    private static async Task<IResult> SendMessage(Guid id, SendChatMessageRequest request, HttpContext context,
        IMirageDbContext db, IHubContext<ChatHub> hub, PresenceTracker presence,
        NotificationService notifications, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id
                && (x.User1Id == userId || x.User2Id == userId)
                && x.Status == MatchStatus.Active, cancellationToken);
        if (match is null) return EndpointHelpers.Forbidden(context);

        var encryptionIdentityCount = await db.ChatEncryptionIdentities.AsNoTracking().CountAsync(
            x => x.UserId == match.User1Id || x.UserId == match.User2Id, cancellationToken);
        if (encryptionIdentityCount != 2)
            return EndpointHelpers.Conflict(context,
                "Encrypted chat will be available after both members secure their messages.");

        // Text and attachment metadata are intentionally inside the authenticated ciphertext.
        var encrypted = request.EncryptionVersion == 1 && !string.IsNullOrWhiteSpace(request.Ciphertext)
            && !string.IsNullOrWhiteSpace(request.EncryptionNonce) && !string.IsNullOrWhiteSpace(request.ClientMessageId);
        if (!encrypted)
            return EndpointHelpers.ValidationProblem(context,
                ("ciphertext", "Direct chat messages must be end-to-end encrypted."));
        if (!string.IsNullOrEmpty(request.Content) || !string.IsNullOrWhiteSpace(request.AttachmentUrl))
            return EndpointHelpers.ValidationProblem(context,
                ("content", "Plaintext content and attachment locations are not accepted for encrypted chat."));
        if (!IsValidEncryptedMessage(request.Ciphertext!, request.EncryptionNonce!))
            return EndpointHelpers.ValidationProblem(context,
                ("ciphertext", "The encrypted message payload is malformed."));

        Message? repliedTo = null;
        if (request.ReplyToMessageId.HasValue)
        {
            repliedTo = await db.Messages.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == request.ReplyToMessageId.Value && x.MatchId == id, cancellationToken);
            if (repliedTo is null)
                return EndpointHelpers.ValidationProblem(context,
                    ("replyToMessageId", "The referenced message does not belong to this conversation."));
        }

        var recipientId = match.User1Id == userId ? match.User2Id : match.User1Id;
        var hadUnread = await db.Messages.AsNoTracking().AnyAsync(
            x => x.MatchId == id && x.SenderId == userId && !x.IsRead, cancellationToken);
        var message = new Message(id, userId, request.Content, request.Type, request.AttachmentUrl,
            request.ReplyToMessageId, encryptedPayload: true);
        try { message.SetEncryptedContent(request.Ciphertext!, request.EncryptionNonce!, request.ClientMessageId!, request.EncryptionVersion); }
        catch (ArgumentException exception) { return EndpointHelpers.ValidationProblem(context, ("ciphertext", exception.Message)); }
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        await db.Matches.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow), cancellationToken);

        await hub.Clients.Group($"match:{id}").SendAsync("ReceiveMessage", new
        {
            message.Id,
            message.MatchId,
            message.SenderId,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            message.Ciphertext,
            message.EncryptionNonce,
            message.ClientMessageId,
            message.EncryptionVersion,
            message.ReplyToMessageId,
            ReplyToSenderId = repliedTo?.SenderId,
            ReplyToContent = repliedTo?.Content,
            ReplyToType = repliedTo?.Type,
            ReplyToAttachmentUrl = repliedTo?.AttachmentUrl,
            SentAt = message.CreatedAt,
            message.IsRead
        }, cancellationToken);

        if (!hadUnread && !presence.IsOnline(recipientId))
        {
            var senderName = await db.Profiles.AsNoTracking().Where(x => x.UserId == userId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "Someone";
            var frontendUrl = (configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app").TrimEnd('/');
            await notifications.NotifyAsync(recipientId, NotificationType.NewMessage,
                $"New message from {senderName}", "You have a new end-to-end encrypted message.",
                id, "Match", cancellationToken, $"{frontendUrl}/matches/{id}/chat", "Open chat");
        }

        return ApiResults.Created(context, $"/api/v1/matching/matches/{id}/messages", new { message.Id },
            "Message sent successfully.");
    }

    private static async Task<IResult> EncryptExistingMessages(Guid id, EncryptExistingMessagesRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.Messages.Length is 0 or > 100)
            return EndpointHelpers.ValidationProblem(context, ("messages", "Provide between 1 and 100 messages."));
        var userId = context.User.GetUserId();
        var inMatch = await db.Matches.AsNoTracking().AnyAsync(x => x.Id == id
            && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (!inMatch) return EndpointHelpers.Forbidden(context);
        var ids = request.Messages.Select(x => x.MessageId).Distinct().ToArray();
        if (ids.Length != request.Messages.Length)
            return EndpointHelpers.ValidationProblem(context, ("messages", "Duplicate message identifiers are not allowed."));
        var messages = await db.Messages.Where(x => x.MatchId == id && ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (messages.Count != ids.Length) return EndpointHelpers.ValidationProblem(context, ("messages", "A message was not found in this conversation."));
        foreach (var replacement in request.Messages)
        {
            var message = messages.Single(x => x.Id == replacement.MessageId);
            if (message.IsEncrypted) continue; // resumable/idempotent migration
            if (!IsValidEncryptedMessage(replacement.Ciphertext, replacement.EncryptionNonce))
                return EndpointHelpers.ValidationProblem(context, ("messages", "An encrypted message payload is malformed."));
            try { message.SetEncryptedContent(replacement.Ciphertext, replacement.EncryptionNonce,
                replacement.ClientMessageId, replacement.EncryptionVersion); }
            catch (ArgumentException exception) { return EndpointHelpers.ValidationProblem(context, ("messages", exception.Message)); }
        }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { Encrypted = messages.Count(x => x.IsEncrypted) }, "Messages encrypted successfully.");
    }

    private static async Task<IResult> MarkRead(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var inMatch = await db.Matches.AsNoTracking()
            .AnyAsync(x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (!inMatch) return EndpointHelpers.Forbidden(context);

        var unread = await db.Messages
            .Where(x => x.MatchId == id && x.SenderId != userId && !x.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
            return ApiResults.Ok(context, new { marked = 0 }, "No unread messages.");

        foreach (var msg in unread) msg.MarkRead();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { marked = unread.Count }, "Messages marked as read.");
    }

    // Chat requests are accepted or declined from the inbox, so that is where a notification
    // email should land — not on the requester's profile.
    private static string InboxUrl(IConfiguration configuration) =>
        $"{(configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app").TrimEnd('/')}/inbox";

    private static bool IsValidEncryptedMessage(string ciphertext, string nonce)
    {
        Span<byte> nonceBytes = stackalloc byte[12];
        if (!Convert.TryFromBase64String(nonce, nonceBytes, out var nonceLength) || nonceLength != 12) return false;
        var maxCiphertextLength = (ciphertext.Length * 3 / 4) + 4;
        if (maxCiphertextLength < 16 || maxCiphertextLength > 9_000) return false;
        var ciphertextBytes = new byte[maxCiphertextLength];
        return Convert.TryFromBase64String(ciphertext, ciphertextBytes, out var ciphertextLength)
            && ciphertextLength >= 16;
    }
}
