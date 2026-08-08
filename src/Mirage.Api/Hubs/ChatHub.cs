using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Services;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Hubs;

[Authorize]
public sealed class ChatHub(
    MirageDbContext db,
    PresenceTracker presence,
    NotificationService notifications,
    IConfiguration configuration) : Hub
{
    // Called when a client connects — join all active match groups immediately
    // so messages arrive regardless of which conversation the client has open.
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var cameOnline = presence.Connect(userId, Context.ConnectionId);
        var matchIds = await db.Matches.AsNoTracking()
            .Where(x => (x.User1Id == userId || x.User2Id == userId) && x.Status == MatchStatus.Active)
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var matchId in matchIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, MatchGroup(matchId));

        var ownMentorProfileId = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
        var acceptedMentorProfileIds = await db.MentorRequests.AsNoTracking()
            .Where(x => x.MenteeUserId == userId && x.Status == MentorRequestStatus.Accepted)
            .Select(x => x.MentorProfileId)
            .ToListAsync();

        var mentorGroupIds = acceptedMentorProfileIds.ToList();
        if (ownMentorProfileId.HasValue) mentorGroupIds.Add(ownMentorProfileId.Value);
        foreach (var mentorProfileId in mentorGroupIds.Distinct())
            await Groups.AddToGroupAsync(Context.ConnectionId, MentorGroup(mentorProfileId));

        var mentorRequestIds = await db.MentorRequests.AsNoTracking()
            .Where(x => x.Status == MentorRequestStatus.Accepted && (x.MenteeUserId == userId || x.Mentor.UserId == userId))
            .Select(x => x.Id)
            .ToListAsync();
        foreach (var mentorRequestId in mentorRequestIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, MentorRequestGroup(mentorRequestId));

        var sessionIds = await db.CounsellingSessions.AsNoTracking()
            .Where(x => (x.ClientUserId == userId || x.Counsellor.UserId == userId
                || (x.PartnerUserId == userId && x.PartnerAccepted))
                && x.Status != SessionStatus.Declined && x.Status != SessionStatus.Cancelled)
            .Select(x => x.Id)
            .ToListAsync();
        foreach (var sessionId in sessionIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, CounsellingGroup(sessionId));

        var friendshipIds = await db.CoupleFriendships.AsNoTracking()
            .Where(f => f.Status == CoupleFriendshipStatus.Active
                && db.Couples.Any(c => (c.Id == f.Couple1Id || c.Id == f.Couple2Id)
                    && (c.User1Id == userId || c.User2Id == userId)))
            .Select(f => f.Id)
            .ToListAsync();
        foreach (var friendshipId in friendshipIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, CoupleFriendGroup(friendshipId));

        if (cameOnline) await BroadcastPresenceAsync(userId, matchIds, isOnline: true, lastSeenAt: null);

        // The peer may have come online while this client was away; SignalR replays nothing,
        // so send the caller a one-off snapshot of who is currently online.
        await SendPresenceSnapshotAsync(userId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (presence.Disconnect(userId, Context.ConnectionId))
        {
            var lastSeenAt = DateTimeOffset.UtcNow;
            await db.Users.Where(x => x.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, lastSeenAt));

            var matchIds = await db.Matches.AsNoTracking()
                .Where(x => (x.User1Id == userId || x.User2Id == userId) && x.Status == MatchStatus.Active)
                .Select(x => x.Id)
                .ToListAsync();
            await BroadcastPresenceAsync(userId, matchIds, isOnline: false, lastSeenAt);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Client → Hub: re-request presence for the caller's peers (used when a tab wakes up).
    public Task RequestPresence() => SendPresenceSnapshotAsync(GetUserId());

    private async Task SendPresenceSnapshotAsync(Guid userId)
    {
        var peers = await db.Matches.AsNoTracking()
            .Where(x => (x.User1Id == userId || x.User2Id == userId) && x.Status == MatchStatus.Active)
            .Select(x => new { MatchId = x.Id, OtherUserId = x.User1Id == userId ? x.User2Id : x.User1Id })
            .ToListAsync();
        if (peers.Count == 0) return;

        var peerIds = peers.Select(x => x.OtherUserId).Distinct().ToList();
        var lastSeen = await db.Users.AsNoTracking()
            .Where(x => peerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.LastSeenAt })
            .ToDictionaryAsync(x => x.Id, x => x.LastSeenAt);

        await Clients.Caller.SendAsync("PresenceSnapshot", peers.Select(peer => new
        {
            peer.MatchId,
            UserId = peer.OtherUserId,
            IsOnline = presence.IsOnline(peer.OtherUserId),
            LastSeenAt = lastSeen.GetValueOrDefault(peer.OtherUserId)
        }));
    }

    // Presence is only shared with people the user is already in an active match with — it is
    // never broadcast platform-wide.
    private async Task BroadcastPresenceAsync(Guid userId, IReadOnlyCollection<Guid> matchIds,
        bool isOnline, DateTimeOffset? lastSeenAt)
    {
        foreach (var matchId in matchIds)
            await Clients.OthersInGroup(MatchGroup(matchId)).SendAsync("PresenceChanged", new
            {
                MatchId = matchId,
                UserId = userId,
                IsOnline = isOnline,
                LastSeenAt = lastSeenAt
            });
    }

    // Client → Hub: join a couple-friendship group created after this connection was opened
    // (the SPA keeps one connection across navigation, so a mid-session befriend would otherwise
    // leave all three participants out of the group until they reload).
    public async Task JoinCoupleFriendship(Guid friendshipId)
    {
        var userId = GetUserId();
        var isParticipant = await db.CoupleFriendships.AsNoTracking().AnyAsync(f => f.Id == friendshipId
            && f.Status == CoupleFriendshipStatus.Active
            && db.Couples.Any(c => (c.Id == f.Couple1Id || c.Id == f.Couple2Id)
                && (c.User1Id == userId || c.User2Id == userId)));
        if (!isParticipant) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, CoupleFriendGroup(friendshipId));
    }

    // Client → Hub: join a match group that became Active after this connection was opened
    // (e.g. a chat request approved mid-session — OnConnectedAsync only joined the groups
    // that were already Active, so without this neither party receives realtime messages
    // until they reload).
    public async Task JoinMatch(Guid matchId)
    {
        var userId = GetUserId();
        var isParticipant = await db.Matches.AsNoTracking().AnyAsync(x => x.Id == matchId
            && (x.User1Id == userId || x.User2Id == userId)
            && x.Status == MatchStatus.Active);
        if (!isParticipant) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, MatchGroup(matchId));
    }

    // Client → Hub: send a message to a couple-friendship thread (friend + both partners)
    public async Task SendCoupleFriendMessage(Guid friendshipId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null)
    {
        content = (content ?? string.Empty).Trim();
        if (type == MessageType.Text && (content.Length == 0 || content.Length > 2000)) return;
        if (type == MessageType.Image && (string.IsNullOrWhiteSpace(attachmentUrl) || content.Length > 2000)) return;

        var userId = GetUserId();
        var isParticipant = await db.CoupleFriendships.AsNoTracking().AnyAsync(f => f.Id == friendshipId
            && f.Status == CoupleFriendshipStatus.Active
            && db.Couples.Any(c => (c.Id == f.Couple1Id || c.Id == f.Couple2Id)
                && (c.User1Id == userId || c.User2Id == userId)));
        if (!isParticipant) return;

        var message = new CoupleFriendMessage(friendshipId, userId, content, type, attachmentUrl);
        db.CoupleFriendMessages.Add(message);
        await db.SaveChangesAsync();
        await db.CoupleFriendships.Where(f => f.Id == friendshipId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.LastActivityAt, DateTimeOffset.UtcNow));

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync();

        await Clients.Group(CoupleFriendGroup(friendshipId)).SendAsync("ReceiveCoupleFriendMessage", new
        {
            message.Id,
            FriendshipId = friendshipId,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        });
    }

    // Client → Hub: send a message to a match
    // Keep the original four-argument hub method stable so ordinary chat continues working
    // during staggered frontend/backend deployments. Replies use a separately named method;
    // SignalR dispatches by method name and argument count rather than C# optional semantics.
    public Task SendMessage(Guid matchId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null) =>
        SendMessageCore(matchId, content, type, attachmentUrl, null);

    public Task SendReply(Guid matchId, string content, MessageType type, string? attachmentUrl,
        Guid replyToMessageId) =>
        SendMessageCore(matchId, content, type, attachmentUrl, replyToMessageId);

    private async Task SendMessageCore(Guid matchId, string content, MessageType type,
        string? attachmentUrl, Guid? replyToMessageId)
    {
        content = (content ?? string.Empty).Trim();
        if (type == MessageType.Text && (content.Length == 0 || content.Length > 2000)) return;
        if (type == MessageType.Image && (string.IsNullOrWhiteSpace(attachmentUrl) || content.Length > 2000)) return;

        var userId = GetUserId();
        var match = await db.Matches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == matchId
                && (x.User1Id == userId || x.User2Id == userId)
                && x.Status == MatchStatus.Active);

        if (match is null) return;
        Message? repliedTo = null;
        if (replyToMessageId.HasValue)
        {
            repliedTo = await db.Messages.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == replyToMessageId.Value && x.MatchId == matchId);
            if (repliedTo is null) return;
        }

        // Read before the insert: an existing unread from this sender means the recipient has
        // already been told about this conversation and does not need telling again.
        var hadUnread = await db.Messages.AsNoTracking()
            .AnyAsync(x => x.MatchId == matchId && x.SenderId == userId && !x.IsRead);

        var message = new Message(matchId, userId, content, type, attachmentUrl, replyToMessageId);
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        await db.Matches.Where(x => x.Id == matchId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow));

        await Clients.Group(MatchGroup(matchId)).SendAsync("ReceiveMessage", new
        {
            message.Id,
            message.MatchId,
            message.SenderId,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            message.ReplyToMessageId,
            ReplyToSenderId = repliedTo?.SenderId,
            ReplyToContent = repliedTo?.Content,
            ReplyToType = repliedTo?.Type,
            ReplyToAttachmentUrl = repliedTo?.AttachmentUrl,
            SentAt = message.CreatedAt,
            message.IsRead
        });

        var recipientId = match.User1Id == userId ? match.User2Id : match.User1Id;
        await NotifyRecipientOfMessageAsync(recipientId, userId, matchId, content, type, hadUnread);
    }

    // A message only earns a notification (and an email) when the recipient is not connected and
    // has no unread message from this sender already waiting. That pair of conditions is what
    // keeps a back-and-forth conversation from turning into a stream of alerts.
    private async Task NotifyRecipientOfMessageAsync(Guid recipientId, Guid senderId, Guid matchId,
        string content, MessageType type, bool hadUnread)
    {
        if (hadUnread || presence.IsOnline(recipientId)) return;

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == senderId).Select(x => x.DisplayName).SingleOrDefaultAsync() ?? "Someone";

        var preview = type == MessageType.Image
            ? "Sent you a photo."
            : content.Length > 140 ? $"{content[..140]}…" : content;

        var frontendUrl = (configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app").TrimEnd('/');
        await notifications.NotifyAsync(recipientId, NotificationType.NewMessage,
            $"New message from {senderName}", preview, matchId, "Match", CancellationToken.None,
            $"{frontendUrl}/matches/{matchId}/chat", "Open chat");
    }

    // Client → Hub: send a message to a mentor's broadcast group (mentor + accepted mentees)
    public async Task SendMentorGroupMessage(Guid mentorProfileId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null)
    {
        content = (content ?? string.Empty).Trim();
        if (type == MessageType.Text && (content.Length == 0 || content.Length > 2000)) return;
        if (type == MessageType.Image && (string.IsNullOrWhiteSpace(attachmentUrl) || content.Length > 2000)) return;

        var userId = GetUserId();
        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.Id == mentorProfileId)
            .Select(x => new { x.UserId, x.AllowMenteesToSeeEachOther })
            .SingleOrDefaultAsync();
        if (mentor is null) return;

        var isMentor = mentor.UserId == userId;
        var isMember = isMentor || await db.MentorRequests.AsNoTracking().AnyAsync(x => x.MentorProfileId == mentorProfileId
            && x.MenteeUserId == userId && x.Status == MentorRequestStatus.Accepted);
        if (!isMember) return;

        var message = new MentorGroupMessage(mentorProfileId, userId, content, type, attachmentUrl);
        db.MentorGroupMessages.Add(message);
        await db.SaveChangesAsync();

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync();

        // Broadcast as one shared payload: fellow mentees' names are only revealed once the
        // mentor opts in, so a non-mentor sender's name is masked unless that's on.
        var broadcastSenderName = isMentor || mentor.AllowMenteesToSeeEachOther ? senderName : "Fellow mentee";

        await Clients.Group(MentorGroup(mentorProfileId)).SendAsync("ReceiveMentorGroupMessage", new
        {
            message.Id,
            MentorProfileId = mentorProfileId,
            message.SenderId,
            SenderName = broadcastSenderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        });
    }

    // Client → Hub: send a message on a mentor↔mentee private 1:1 channel
    public async Task SendMentorMessage(Guid mentorRequestId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null)
    {
        content = (content ?? string.Empty).Trim();
        if (type == MessageType.Text && (content.Length == 0 || content.Length > 2000)) return;
        if (type == MessageType.Image && (string.IsNullOrWhiteSpace(attachmentUrl) || content.Length > 2000)) return;

        var userId = GetUserId();
        var isParty = await db.MentorRequests.AsNoTracking().AnyAsync(x => x.Id == mentorRequestId
            && (x.MenteeUserId == userId || x.Mentor.UserId == userId)
            && x.Status == MentorRequestStatus.Accepted);
        if (!isParty) return;

        var message = new MentorMessage(mentorRequestId, userId, content, type, attachmentUrl);
        db.MentorMessages.Add(message);
        await db.SaveChangesAsync();

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync();

        await Clients.Group(MentorRequestGroup(mentorRequestId)).SendAsync("ReceiveMentorMessage", new
        {
            message.Id,
            MentorRequestId = mentorRequestId,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        });
    }

    // Client → Hub: send a message on a counselling session's private channel
    public async Task SendCounsellingMessage(Guid sessionId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null)
    {
        content = (content ?? string.Empty).Trim();
        if (type == MessageType.Text && (content.Length == 0 || content.Length > 2000)) return;
        if (type == MessageType.Image && (string.IsNullOrWhiteSpace(attachmentUrl) || content.Length > 2000)) return;

        var userId = GetUserId();
        var isParty = await db.CounsellingSessions.AsNoTracking().AnyAsync(x => x.Id == sessionId
            && (x.ClientUserId == userId || x.Counsellor.UserId == userId
                || (x.PartnerUserId == userId && x.PartnerAccepted))
            && x.Status != SessionStatus.Declined && x.Status != SessionStatus.Cancelled);
        if (!isParty) return;

        var message = new CounsellingMessage(sessionId, userId, content, type, attachmentUrl);
        db.CounsellingMessages.Add(message);
        await db.SaveChangesAsync();

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync();

        await Clients.Group(CounsellingGroup(sessionId)).SendAsync("ReceiveCounsellingMessage", new
        {
            message.Id,
            SessionId = sessionId,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        });
    }

    // Client → Hub: mark all messages in a match as read
    public async Task MarkRead(Guid matchId)
    {
        var userId = GetUserId();

        var inMatch = await db.Matches.AsNoTracking()
            .AnyAsync(x => x.Id == matchId
                && (x.User1Id == userId || x.User2Id == userId)
                && x.Status == MatchStatus.Active);
        if (!inMatch) return;

        var unread = await db.Messages
            .Where(x => x.MatchId == matchId && x.SenderId != userId && !x.IsRead)
            .ToListAsync();

        if (unread.Count == 0) return;

        foreach (var msg in unread) msg.MarkRead();
        await db.SaveChangesAsync();

        // Notify all clients in the match group (including sender) that messages were read
        await Clients.Group(MatchGroup(matchId)).SendAsync("MessagesRead", new
        {
            MatchId = matchId,
            ReadBy = userId,
            ReadAt = DateTimeOffset.UtcNow
        });
    }

    // Client → Hub: notify the other party the user is typing
    public async Task Typing(Guid matchId)
    {
        var userId = GetUserId();
        var inMatch = await db.Matches.AsNoTracking()
            .AnyAsync(x => x.Id == matchId
                && (x.User1Id == userId || x.User2Id == userId)
                && x.Status == MatchStatus.Active);
        if (!inMatch) return;

        // Broadcast to the group but exclude the caller
        await Clients.OthersInGroup(MatchGroup(matchId)).SendAsync("UserTyping", new
        {
            MatchId = matchId,
            UserId = userId
        });
    }

    private Guid GetUserId() =>
        Guid.Parse(Context.User!.FindFirstValue("sub")
            ?? throw new InvalidOperationException("User ID claim is missing."));

    private static string MatchGroup(Guid matchId) => $"match:{matchId}";
    private static string MentorGroup(Guid mentorProfileId) => $"mentorgroup:{mentorProfileId}";
    private static string MentorRequestGroup(Guid mentorRequestId) => $"mentorrequest:{mentorRequestId}";
    private static string CounsellingGroup(Guid sessionId) => $"counsellingsession:{sessionId}";
    private static string CoupleFriendGroup(Guid friendshipId) => $"couplefriend:{friendshipId}";
}
