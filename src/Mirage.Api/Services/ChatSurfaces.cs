using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Common;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

/// <summary>Which kind of conversation a chat action is addressed to.</summary>
public enum ChatSurfaceKind
{
    Match,
    CoupleFriend,
    MentorRequest,
    MentorGroup,
    CounsellingSession,
    CounsellorGroup,
}

/// <summary>
/// One conversation, wherever it lives. Mirage holds six separate chats in six separate tables —
/// a match, two befriended couples, a mentor's private channel, a mentor's group, a counselling
/// session, a counsellor's group — and wallpapers, hiding a message and clearing a history all
/// mean the same thing in each of them. This is the name they share.
/// </summary>
/// <remarks>
/// <see cref="Key"/> deliberately reproduces the ChatHub group names ("match:{id}") so a
/// conversation reads the same in the database, in the API and on the wire.
/// </remarks>
public sealed record ChatSurface(ChatSurfaceKind Kind, Guid Id)
{
    public string Key => $"{Slug(Kind)}:{Id}";

    public static string Slug(ChatSurfaceKind kind) => kind switch
    {
        ChatSurfaceKind.Match => "match",
        ChatSurfaceKind.CoupleFriend => "couplefriend",
        ChatSurfaceKind.MentorRequest => "mentorrequest",
        ChatSurfaceKind.MentorGroup => "mentorgroup",
        ChatSurfaceKind.CounsellingSession => "counsellingsession",
        ChatSurfaceKind.CounsellorGroup => "counsellorgroup",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Resolves the URL's surface segment, which is a slug rather than an enum name.</summary>
    public static bool TryParse(string? surface, Guid id, out ChatSurface result)
    {
        foreach (var kind in Enum.GetValues<ChatSurfaceKind>())
        {
            if (!string.Equals(Slug(kind), surface, StringComparison.OrdinalIgnoreCase)) continue;
            result = new ChatSurface(kind, id);
            return true;
        }

        result = null!;
        return false;
    }
}

/// <summary>A message located in whichever table its conversation keeps.</summary>
public sealed record ChatMessageRef(Guid Id, Guid SenderId, DateTimeOffset SentAt);

/// <summary>
/// What one member may see of a conversation: everything except the messages they deleted for
/// themselves and anything sent before they last cleared it.
/// </summary>
/// <remarks>
/// Resolved once per request and then pushed into the message query, so a member who has hidden
/// nothing (the overwhelming majority) pays two cheap indexed lookups and no extra predicates.
/// </remarks>
public sealed record ChatVisibility(HashSet<Guid> HiddenMessageIds, DateTimeOffset? ClearedAt)
{
    public static readonly ChatVisibility Unrestricted = new([], null);

    public bool IsUnrestricted => HiddenMessageIds.Count == 0 && ClearedAt is null;

    public IQueryable<T> Apply<T>(IQueryable<T> query) where T : Entity
    {
        if (ClearedAt is { } clearedAt) query = query.Where(x => x.CreatedAt > clearedAt);
        if (HiddenMessageIds.Count > 0)
        {
            // Materialised into a local so EF parameterises the list rather than closing over the
            // record instance.
            var hidden = HiddenMessageIds.ToArray();
            query = query.Where(x => !hidden.Contains(x.Id));
        }

        return query;
    }

    public static async Task<ChatVisibility> ForAsync(IMirageDbContext db, Guid userId, string conversationKey,
        CancellationToken cancellationToken)
    {
        var hidden = await db.ChatMessageHides.AsNoTracking()
            .Where(x => x.UserId == userId && x.ConversationKey == conversationKey)
            .Select(x => x.MessageId)
            .ToListAsync(cancellationToken);
        var clearedAt = await db.ChatClearMarkers.AsNoTracking()
            .Where(x => x.UserId == userId && x.ConversationKey == conversationKey)
            .Select(x => (DateTimeOffset?)x.ClearedAt)
            .SingleOrDefaultAsync(cancellationToken);
        return hidden.Count == 0 && clearedAt is null ? Unrestricted : new ChatVisibility([.. hidden], clearedAt);
    }
}

/// <summary>
/// The one place that knows how each of Mirage's conversations decides who belongs to it and
/// where its messages are stored.
/// </summary>
public sealed class ChatSurfaceService(IMirageDbContext db)
{
    /// <summary>
    /// How long after sending a message it can still be taken back from everyone. After this the
    /// only deletion left is "delete for me": by then the other side has almost certainly read it,
    /// and quietly rewriting what someone remembers reading is not a power worth handing out.
    /// </summary>
    public static readonly TimeSpan DeleteForEveryoneWindow = TimeSpan.FromMinutes(5);

    public Task<bool> IsParticipantAsync(ChatSurface surface, Guid userId, CancellationToken cancellationToken) =>
        surface.Kind switch
        {
            ChatSurfaceKind.Match => db.Matches.AsNoTracking()
                .AnyAsync(x => x.Id == surface.Id && (x.User1Id == userId || x.User2Id == userId), cancellationToken),

            // All four spouses share the one thread, so membership is "in either couple".
            ChatSurfaceKind.CoupleFriend => db.CoupleFriendships.AsNoTracking()
                .AnyAsync(f => f.Id == surface.Id
                    && db.Couples.Any(c => (c.Id == f.Couple1Id || c.Id == f.Couple2Id)
                        && (c.User1Id == userId || c.User2Id == userId)), cancellationToken),

            ChatSurfaceKind.MentorRequest => db.MentorRequests.AsNoTracking()
                .AnyAsync(x => x.Id == surface.Id
                    && (x.MenteeUserId == userId || x.Mentor.UserId == userId)
                    && x.Status == MentorRequestStatus.Accepted, cancellationToken),

            ChatSurfaceKind.MentorGroup => db.Mentors.AsNoTracking()
                .AnyAsync(x => x.Id == surface.Id && x.UserId == userId, cancellationToken)
                .ContinueWithParticipant(() => db.MentorRequests.AsNoTracking()
                    .AnyAsync(x => x.MentorProfileId == surface.Id && x.MenteeUserId == userId
                        && x.Status == MentorRequestStatus.Accepted, cancellationToken)),

            ChatSurfaceKind.CounsellingSession => db.CounsellingSessions.AsNoTracking()
                .AnyAsync(x => x.Id == surface.Id
                    && (x.ClientUserId == userId || x.Counsellor.UserId == userId
                        || (x.PartnerUserId == userId && x.PartnerAccepted)), cancellationToken),

            ChatSurfaceKind.CounsellorGroup => db.Counsellors.AsNoTracking()
                .AnyAsync(x => x.Id == surface.Id && x.UserId == userId, cancellationToken)
                .ContinueWithParticipant(() => db.CounsellingSessions.AsNoTracking()
                    .AnyAsync(x => x.CounsellorId == surface.Id
                        && (x.ClientUserId == userId || (x.PartnerUserId == userId && x.PartnerAccepted))
                        && x.Status != SessionStatus.Declined && x.Status != SessionStatus.Cancelled,
                        cancellationToken)),

            _ => Task.FromResult(false),
        };

    /// <summary>Every conversation the member belongs to, named the way the wire names them.</summary>
    /// <remarks>
    /// Backs the reads that are addressed to a member rather than to one conversation — loading
    /// the wallpapers of every thread at once, for instance — so they can be answered with a
    /// single indexed lookup per surface instead of a membership check per row.
    /// </remarks>
    public async Task<List<string>> ConversationKeysAsync(Guid userId, CancellationToken cancellationToken)
    {
        var keys = new List<string>();

        void Add(ChatSurfaceKind kind, IEnumerable<Guid> ids)
        {
            foreach (var id in ids) keys.Add(new ChatSurface(kind, id).Key);
        }

        Add(ChatSurfaceKind.Match, await db.Matches.AsNoTracking()
            .Where(x => x.User1Id == userId || x.User2Id == userId)
            .Select(x => x.Id).ToListAsync(cancellationToken));

        Add(ChatSurfaceKind.CoupleFriend, await db.CoupleFriendships.AsNoTracking()
            .Where(f => db.Couples.Any(c => (c.Id == f.Couple1Id || c.Id == f.Couple2Id)
                && (c.User1Id == userId || c.User2Id == userId)))
            .Select(f => f.Id).ToListAsync(cancellationToken));

        var mentorRequests = await db.MentorRequests.AsNoTracking()
            .Where(x => x.Status == MentorRequestStatus.Accepted
                && (x.MenteeUserId == userId || x.Mentor.UserId == userId))
            .Select(x => new { x.Id, x.MentorProfileId })
            .ToListAsync(cancellationToken);
        Add(ChatSurfaceKind.MentorRequest, mentorRequests.Select(x => x.Id));

        var mentorGroupIds = mentorRequests.Select(x => x.MentorProfileId).ToList();
        mentorGroupIds.AddRange(await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken));
        Add(ChatSurfaceKind.MentorGroup, mentorGroupIds.Distinct());

        var sessions = await db.CounsellingSessions.AsNoTracking()
            .Where(x => (x.ClientUserId == userId || x.Counsellor.UserId == userId
                    || (x.PartnerUserId == userId && x.PartnerAccepted))
                && x.Status != SessionStatus.Declined && x.Status != SessionStatus.Cancelled)
            .Select(x => new { x.Id, x.CounsellorId })
            .ToListAsync(cancellationToken);
        Add(ChatSurfaceKind.CounsellingSession, sessions.Select(x => x.Id));

        var counsellorGroupIds = sessions.Select(x => x.CounsellorId).ToList();
        counsellorGroupIds.AddRange(await db.Counsellors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(cancellationToken));
        Add(ChatSurfaceKind.CounsellorGroup, counsellorGroupIds.Distinct());

        return keys;
    }

    /// <summary>Finds a message by id, but only within the conversation it is claimed to belong to.</summary>
    public Task<ChatMessageRef?> FindMessageAsync(ChatSurface surface, Guid messageId,
        CancellationToken cancellationToken) =>
        surface.Kind switch
        {
            ChatSurfaceKind.Match => db.Messages.AsNoTracking()
                .Where(x => x.Id == messageId && x.MatchId == surface.Id)
                .Select(x => new ChatMessageRef(x.Id, x.SenderId, x.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken),

            ChatSurfaceKind.CoupleFriend => db.CoupleFriendMessages.AsNoTracking()
                .Where(x => x.Id == messageId && x.FriendshipId == surface.Id)
                .Select(x => new ChatMessageRef(x.Id, x.SenderId, x.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken),

            ChatSurfaceKind.MentorRequest => db.MentorMessages.AsNoTracking()
                .Where(x => x.Id == messageId && x.MentorRequestId == surface.Id)
                .Select(x => new ChatMessageRef(x.Id, x.SenderId, x.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken),

            ChatSurfaceKind.MentorGroup => db.MentorGroupMessages.AsNoTracking()
                .Where(x => x.Id == messageId && x.MentorProfileId == surface.Id)
                .Select(x => new ChatMessageRef(x.Id, x.SenderId, x.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken),

            ChatSurfaceKind.CounsellingSession => db.CounsellingMessages.AsNoTracking()
                .Where(x => x.Id == messageId && x.SessionId == surface.Id)
                .Select(x => new ChatMessageRef(x.Id, x.SenderId, x.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken),

            ChatSurfaceKind.CounsellorGroup => db.CounsellorGroupMessages.AsNoTracking()
                .Where(x => x.Id == messageId && x.CounsellorProfileId == surface.Id)
                .Select(x => new ChatMessageRef(x.Id, x.SenderId, x.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken),

            _ => Task.FromResult<ChatMessageRef?>(null),
        };

    /// <summary>
    /// Removes messages from the conversation for everyone in it.
    /// </summary>
    /// <remarks>
    /// A real delete, not a tombstone: "delete for everyone" is a privacy promise, and a row kept
    /// behind a flag is still a row holding what was said. The clients drop the bubble on the
    /// hub's DeletedMessages event and simply never see it again after a reload.
    /// </remarks>
    public Task<int> DeleteForEveryoneAsync(ChatSurface surface, IReadOnlyCollection<Guid> messageIds,
        CancellationToken cancellationToken)
    {
        var ids = messageIds.ToArray();
        return surface.Kind switch
        {
            ChatSurfaceKind.Match => db.Messages
                .Where(x => x.MatchId == surface.Id && ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken),
            ChatSurfaceKind.CoupleFriend => db.CoupleFriendMessages
                .Where(x => x.FriendshipId == surface.Id && ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken),
            ChatSurfaceKind.MentorRequest => db.MentorMessages
                .Where(x => x.MentorRequestId == surface.Id && ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken),
            ChatSurfaceKind.MentorGroup => db.MentorGroupMessages
                .Where(x => x.MentorProfileId == surface.Id && ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken),
            ChatSurfaceKind.CounsellingSession => db.CounsellingMessages
                .Where(x => x.SessionId == surface.Id && ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken),
            ChatSurfaceKind.CounsellorGroup => db.CounsellorGroupMessages
                .Where(x => x.CounsellorProfileId == surface.Id && ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken),
            _ => Task.FromResult(0),
        };
    }

    /// <summary>The id every message in this conversation still visible to the caller.</summary>
    /// <remarks>Backs "clear chat" and "select all", both of which need the ids, not the bodies.</remarks>
    public async Task<List<Guid>> MessageIdsAsync(ChatSurface surface, ChatVisibility visibility,
        CancellationToken cancellationToken) =>
        surface.Kind switch
        {
            ChatSurfaceKind.Match => await visibility
                .Apply(db.Messages.AsNoTracking().Where(x => x.MatchId == surface.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken),
            ChatSurfaceKind.CoupleFriend => await visibility
                .Apply(db.CoupleFriendMessages.AsNoTracking().Where(x => x.FriendshipId == surface.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken),
            ChatSurfaceKind.MentorRequest => await visibility
                .Apply(db.MentorMessages.AsNoTracking().Where(x => x.MentorRequestId == surface.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken),
            ChatSurfaceKind.MentorGroup => await visibility
                .Apply(db.MentorGroupMessages.AsNoTracking().Where(x => x.MentorProfileId == surface.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken),
            ChatSurfaceKind.CounsellingSession => await visibility
                .Apply(db.CounsellingMessages.AsNoTracking().Where(x => x.SessionId == surface.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken),
            ChatSurfaceKind.CounsellorGroup => await visibility
                .Apply(db.CounsellorGroupMessages.AsNoTracking().Where(x => x.CounsellorProfileId == surface.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken),
            _ => [],
        };
}

internal static class ChatParticipantTaskExtensions
{
    /// <summary>Short-circuiting "or" over two membership checks, so the second query only runs if needed.</summary>
    public static async Task<bool> ContinueWithParticipant(this Task<bool> first, Func<Task<bool>> second) =>
        await first || await second();
}
