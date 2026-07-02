using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Hubs;

[Authorize]
public sealed class ChatHub(MirageDbContext db) : Hub
{
    // Called when a client connects — join all active match groups immediately
    // so messages arrive regardless of which conversation the client has open.
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
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

        var sessionIds = await db.CounsellingSessions.AsNoTracking()
            .Where(x => (x.ClientUserId == userId || x.Counsellor.UserId == userId)
                && x.Status != SessionStatus.Requested && x.Status != SessionStatus.Declined)
            .Select(x => x.Id)
            .ToListAsync();
        foreach (var sessionId in sessionIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, CounsellingGroup(sessionId));

        await base.OnConnectedAsync();
    }

    // Client → Hub: send a message to a match
    public async Task SendMessage(Guid matchId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null)
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

        var message = new Message(matchId, userId, content, type, attachmentUrl);
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        await Clients.Group(MatchGroup(matchId)).SendAsync("ReceiveMessage", new
        {
            message.Id,
            message.MatchId,
            message.SenderId,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt,
            message.IsRead
        });
    }

    // Client → Hub: send a message to a mentor's broadcast group (mentor + accepted mentees)
    public async Task SendMentorGroupMessage(Guid mentorProfileId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null)
    {
        content = (content ?? string.Empty).Trim();
        if (type == MessageType.Text && (content.Length == 0 || content.Length > 2000)) return;
        if (type == MessageType.Image && (string.IsNullOrWhiteSpace(attachmentUrl) || content.Length > 2000)) return;

        var userId = GetUserId();
        var isMember = await db.Mentors.AsNoTracking().AnyAsync(x => x.Id == mentorProfileId && x.UserId == userId)
            || await db.MentorRequests.AsNoTracking().AnyAsync(x => x.MentorProfileId == mentorProfileId
                && x.MenteeUserId == userId && x.Status == MentorRequestStatus.Accepted);
        if (!isMember) return;

        var message = new MentorGroupMessage(mentorProfileId, userId, content, type, attachmentUrl);
        db.MentorGroupMessages.Add(message);
        await db.SaveChangesAsync();

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync();

        await Clients.Group(MentorGroup(mentorProfileId)).SendAsync("ReceiveMentorGroupMessage", new
        {
            message.Id,
            MentorProfileId = mentorProfileId,
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
            && (x.ClientUserId == userId || x.Counsellor.UserId == userId)
            && x.Status != SessionStatus.Requested && x.Status != SessionStatus.Declined);
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
    private static string CounsellingGroup(Guid sessionId) => $"counsellingsession:{sessionId}";
}
