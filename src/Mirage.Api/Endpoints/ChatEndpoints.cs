using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Hubs;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

// Everything that is true of a conversation whichever conversation it is: the wallpaper it wears,
// the emoji hung off its messages, the messages a member deleted from their own copy, the history
// they cleared, and taking a message back from everyone inside the five-minute window.
//
// Deliberately one surface-agnostic route family rather than a delete endpoint bolted onto each of
// the six chats — the rules do not differ by chat, only the table does, and that difference lives
// in ChatSurfaceService.
internal static class ChatEndpoints
{
    public static RouteGroupBuilder MapChatEndpoints(this RouteGroupBuilder api)
    {
        var chats = api.MapGroup("/chats").WithTags("Chats").RequireAuthorization();

        // Wallpapers. The default is fetched once at start-up alongside the overrides, so opening
        // a conversation needs no extra round trip before it can paint.
        chats.MapGet("/themes", GetThemes);
        chats.MapPut("/themes/default", SetDefaultTheme);
        chats.MapPut("/{surface}/{id:guid}/theme", SetConversationTheme);
        chats.MapDelete("/{surface}/{id:guid}/theme", ClearConversationTheme);

        // Reactions. Read with the thread, written one message at a time.
        chats.MapGet("/{surface}/{id:guid}/reactions", GetReactions);
        chats.MapPut("/{surface}/{id:guid}/messages/{messageId:guid}/reaction", SetReaction);
        chats.MapDelete("/{surface}/{id:guid}/messages/{messageId:guid}/reaction", ClearReaction);

        // Deletion.
        chats.MapPost("/{surface}/{id:guid}/messages/delete", DeleteMessages);
        chats.MapPost("/{surface}/{id:guid}/clear", ClearConversation);

        return chats;
    }

    // ------------------------------------------------------------------ wallpapers

    private static async Task<IResult> GetThemes(HttpContext context, IMirageDbContext db,
        ChatSurfaceService surfaces, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();

        var defaultTheme = await db.ChatThemePreferences.AsNoTracking()
            .Where(x => x.UserId == userId && x.ConversationKey == ChatThemePreference.AccountDefaultKey)
            .Select(x => x.ThemeKey)
            .SingleOrDefaultAsync(cancellationToken);

        // Filtered by membership rather than returned wholesale: a conversation's key is its id,
        // and handing back the keys of threads the caller is not in would leak who exists.
        var keys = await surfaces.ConversationKeysAsync(userId, cancellationToken);
        var overrides = keys.Count == 0
            ? []
            : await db.ChatConversationThemes.AsNoTracking()
                .Where(x => keys.Contains(x.ConversationKey))
                .Select(x => new ChatThemeOverrideResponse(x.ConversationKey, x.ThemeKey))
                .ToListAsync(cancellationToken);

        return ApiResults.Ok(context, new ChatThemesResponse(defaultTheme, overrides),
            "Chat themes retrieved successfully.");
    }

    /// <summary>The caller's own fallback wallpaper, private to them.</summary>
    private static async Task<IResult> SetDefaultTheme(SetChatThemeRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (ReadThemeKey(request, context, out var themeKey, out var invalid)) return invalid;

        var userId = context.User.GetUserId();
        var key = ChatThemePreference.AccountDefaultKey;
        var existing = await db.ChatThemePreferences
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ConversationKey == key, cancellationToken);
        if (existing is null) db.ChatThemePreferences.Add(new ChatThemePreference(userId, key, themeKey));
        else existing.SetTheme(themeKey);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context, new ChatThemeOverrideResponse(key, themeKey),
            "Chat theme saved successfully.");
    }

    /// <summary>
    /// The wallpaper this conversation wears — for everyone in it.
    /// </summary>
    /// <remarks>
    /// Shared rather than personal: a thread is a place two or more people are in together, and it
    /// looking the same to all of them is what makes "let's change the wallpaper" mean anything.
    /// The hub push is what keeps the other screens honest; a client that missed it picks the
    /// change up on its next themes load.
    /// </remarks>
    private static async Task<IResult> SetConversationTheme(string surface, Guid id, SetChatThemeRequest request,
        HttpContext context, IMirageDbContext db, ChatSurfaceService surfaces, IHubContext<ChatHub> hub,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;
        if (ReadThemeKey(request, context, out var themeKey, out var invalid)) return invalid;

        var userId = context.User.GetUserId();
        var key = resolved.Surface.Key;
        var existing = await db.ChatConversationThemes
            .SingleOrDefaultAsync(x => x.ConversationKey == key, cancellationToken);
        if (existing is null) db.ChatConversationThemes.Add(new ChatConversationTheme(key, themeKey, userId));
        else existing.SetTheme(themeKey, userId);
        await db.SaveChangesAsync(cancellationToken);

        await BroadcastThemeAsync(hub, resolved.Surface, themeKey, userId, cancellationToken);
        return ApiResults.Ok(context, new ChatThemeOverrideResponse(key, themeKey),
            "Chat theme saved successfully.");
    }

    /// <summary>Puts the conversation back on whatever each member's own default happens to be.</summary>
    private static async Task<IResult> ClearConversationTheme(string surface, Guid id, HttpContext context,
        IMirageDbContext db, ChatSurfaceService surfaces, IHubContext<ChatHub> hub,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;

        var userId = context.User.GetUserId();
        var key = resolved.Surface.Key;
        var existing = await db.ChatConversationThemes
            .SingleOrDefaultAsync(x => x.ConversationKey == key, cancellationToken);
        if (existing is not null)
        {
            db.ChatConversationThemes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await BroadcastThemeAsync(hub, resolved.Surface, null, userId, cancellationToken);
        }

        // What the caller falls back to is their own default, which is not what the other side
        // falls back to — so the null theme on the wire means "no shared wallpaper" and each
        // client resolves its own.
        return ApiResults.Ok(context, new ChatThemeOverrideResponse(key, null),
            "Chat theme reset successfully.");
    }

    /// <summary>Validates the requested theme key, returning true when it is unusable.</summary>
    private static bool ReadThemeKey(SetChatThemeRequest request, HttpContext context, out string themeKey,
        out IResult invalid)
    {
        themeKey = request.Theme?.Trim() ?? string.Empty;
        if (themeKey.Length == 0)
        {
            invalid = EndpointHelpers.ValidationProblem(context, ("theme", "A theme is required."));
            return true;
        }

        if (themeKey.Length > 60)
        {
            invalid = EndpointHelpers.ValidationProblem(context,
                ("theme", "Theme keys must be 60 characters or fewer."));
            return true;
        }

        invalid = null!;
        return false;
    }

    private static Task BroadcastThemeAsync(IHubContext<ChatHub> hub, ChatSurface chat, string? themeKey,
        Guid userId, CancellationToken cancellationToken) =>
        hub.Clients.Group(chat.Key).SendAsync("ChatThemeChanged", new
        {
            ConversationKey = chat.Key,
            Surface = ChatSurface.Slug(chat.Kind),
            ConversationId = chat.Id,
            Theme = themeKey,
            SetByUserId = userId,
        }, cancellationToken);

    // ------------------------------------------------------------------ reactions

    private static async Task<IResult> GetReactions(string surface, Guid id, HttpContext context,
        IMirageDbContext db, ChatSurfaceService surfaces, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;

        var userId = context.User.GetUserId();
        var rows = await db.ChatMessageReactions.AsNoTracking()
            .Where(x => x.ConversationKey == resolved.Surface.Key)
            .Select(x => new { x.MessageId, x.UserId, x.Emoji })
            .ToListAsync(cancellationToken);

        // Folded here rather than in the database: a thread's reactions are a handful of rows, and
        // grouping them in memory keeps the shape the clients render in one place.
        var messages = rows
            .GroupBy(x => x.MessageId)
            .Select(message => new ChatMessageReactionsResponse(message.Key, message
                .GroupBy(x => x.Emoji)
                .Select(emoji => new ChatReactionGroupResponse(
                    emoji.Key,
                    emoji.Count(),
                    emoji.Any(x => x.UserId == userId),
                    emoji.Select(x => x.UserId).ToList()))
                .OrderByDescending(x => x.Count)
                .ToList()))
            .ToList();

        return ApiResults.Ok(context, messages, "Chat reactions retrieved successfully.");
    }

    /// <summary>
    /// Hangs an emoji off a message, replacing whatever the caller had chosen before.
    /// </summary>
    /// <remarks>
    /// Reacting to a message you have hidden for yourself is allowed and harmless — the row is
    /// keyed to the message, and the caller simply cannot see what they reacted to.
    /// </remarks>
    private static async Task<IResult> SetReaction(string surface, Guid id, Guid messageId,
        SetChatReactionRequest request, HttpContext context, IMirageDbContext db, ChatSurfaceService surfaces,
        IHubContext<ChatHub> hub, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;

        var emoji = request.Emoji?.Trim() ?? string.Empty;
        if (emoji.Length == 0)
            return EndpointHelpers.ValidationProblem(context, ("emoji", "An emoji is required."));
        if (emoji.Length > ChatMessageReaction.MaxEmojiLength)
            return EndpointHelpers.ValidationProblem(context, ("emoji", "That is not a single emoji."));

        var chat = resolved.Surface;
        if (await surfaces.FindMessageAsync(chat, messageId, cancellationToken) is null)
            return EndpointHelpers.NotFound(context, "That message is no longer in this conversation.");

        var userId = context.User.GetUserId();
        var existing = await db.ChatMessageReactions
            .SingleOrDefaultAsync(x => x.UserId == userId && x.MessageId == messageId, cancellationToken);
        if (existing is null) db.ChatMessageReactions.Add(new ChatMessageReaction(userId, chat.Key, messageId, emoji));
        else existing.SetEmoji(emoji);
        await db.SaveChangesAsync(cancellationToken);

        await BroadcastReactionAsync(hub, chat, messageId, userId, emoji, cancellationToken);
        return ApiResults.Ok(context, new ChatReactionGroupResponse(emoji, 1, true, [userId]),
            "Reaction added successfully.");
    }

    private static async Task<IResult> ClearReaction(string surface, Guid id, Guid messageId, HttpContext context,
        IMirageDbContext db, ChatSurfaceService surfaces, IHubContext<ChatHub> hub,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;

        var userId = context.User.GetUserId();
        var existing = await db.ChatMessageReactions
            .SingleOrDefaultAsync(x => x.UserId == userId && x.MessageId == messageId, cancellationToken);
        if (existing is not null)
        {
            db.ChatMessageReactions.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await BroadcastReactionAsync(hub, resolved.Surface, messageId, userId, null, cancellationToken);
        }

        return ApiResults.Ok(context, new ChatMessageReactionsResponse(messageId, []),
            "Reaction removed successfully.");
    }

    /// <remarks>
    /// A delta rather than the message's whole reaction set: one member's emoji changed, and every
    /// client already holds the rest. A null emoji means they took theirs back.
    /// </remarks>
    private static Task BroadcastReactionAsync(IHubContext<ChatHub> hub, ChatSurface chat, Guid messageId,
        Guid userId, string? emoji, CancellationToken cancellationToken) =>
        hub.Clients.Group(chat.Key).SendAsync("MessageReaction", new
        {
            ConversationKey = chat.Key,
            Surface = ChatSurface.Slug(chat.Kind),
            ConversationId = chat.Id,
            MessageId = messageId,
            UserId = userId,
            Emoji = emoji,
        }, cancellationToken);

    // ------------------------------------------------------------------ deletion

    private static async Task<IResult> DeleteMessages(string surface, Guid id, DeleteChatMessagesRequest request,
        HttpContext context, IMirageDbContext db, ChatSurfaceService surfaces, IHubContext<ChatHub> hub,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;

        var messageIds = (request.MessageIds ?? []).Distinct().ToArray();
        if (messageIds.Length == 0)
            return EndpointHelpers.ValidationProblem(context, ("messageIds", "Select at least one message."));
        // A bulk delete is a selection someone made by hand; anything beyond a screenful of taps is
        // a script, and clearing a whole history has its own endpoint.
        if (messageIds.Length > 200)
            return EndpointHelpers.ValidationProblem(context, ("messageIds", "Delete 200 messages or fewer at a time."));

        var userId = context.User.GetUserId();
        var chat = resolved.Surface;

        if (!request.ForEveryone)
        {
            var hidden = await HideAsync(db, userId, chat, messageIds, surfaces, cancellationToken);
            return ApiResults.Ok(context, new DeleteChatMessagesResponse(hidden, [], false),
                "Messages deleted for you.");
        }

        // Delete for everyone: only your own messages, and only while the window is open. Anything
        // in the selection that fails either test is still removed from the caller's own copy —
        // a mixed selection should do as much as it is allowed to rather than refuse wholesale.
        var cutoff = DateTimeOffset.UtcNow - ChatSurfaceService.DeleteForEveryoneWindow;
        var forEveryone = new List<Guid>();
        var forCallerOnly = new List<Guid>();
        foreach (var messageId in messageIds)
        {
            var message = await surfaces.FindMessageAsync(chat, messageId, cancellationToken);
            if (message is null) continue;
            if (message.SenderId == userId && message.SentAt >= cutoff) forEveryone.Add(messageId);
            else forCallerOnly.Add(messageId);
        }

        if (forEveryone.Count > 0)
        {
            await surfaces.DeleteForEveryoneAsync(chat, forEveryone, cancellationToken);
            // Hides pointing at rows that no longer exist are dead weight, and a member who had
            // hidden a message that is then deleted for everyone would otherwise keep the row.
            await db.ChatMessageHides
                .Where(x => x.ConversationKey == chat.Key && forEveryone.Contains(x.MessageId))
                .ExecuteDeleteAsync(cancellationToken);
            // Likewise the reactions: nothing is left to hang off, and a stale row would come back
            // as a floating emoji if the id were ever reused.
            await db.ChatMessageReactions
                .Where(x => x.ConversationKey == chat.Key && forEveryone.Contains(x.MessageId))
                .ExecuteDeleteAsync(cancellationToken);
            await hub.Clients.Group(chat.Key).SendAsync("MessagesDeleted", new
            {
                ConversationKey = chat.Key,
                Surface = ChatSurface.Slug(chat.Kind),
                ConversationId = chat.Id,
                MessageIds = forEveryone,
                DeletedByUserId = userId,
            }, cancellationToken);
        }

        var hiddenForCaller = forCallerOnly.Count == 0
            ? []
            : await HideAsync(db, userId, chat, forCallerOnly, surfaces, cancellationToken);

        return ApiResults.Ok(context,
            new DeleteChatMessagesResponse(hiddenForCaller, forEveryone, forCallerOnly.Count > 0),
            forEveryone.Count == 0
                ? "Those messages could only be deleted for you."
                : forCallerOnly.Count == 0
                    ? "Messages deleted for everyone."
                    : "Some messages could only be deleted for you.");
    }

    private static async Task<IResult> ClearConversation(string surface, Guid id, HttpContext context,
        IMirageDbContext db, ChatSurfaceService surfaces, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;

        var userId = context.User.GetUserId();
        var key = resolved.Surface.Key;
        var clearedAt = DateTimeOffset.UtcNow;

        var marker = await db.ChatClearMarkers
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ConversationKey == key, cancellationToken);
        if (marker is null) db.ChatClearMarkers.Add(new ChatClearMarker(userId, key, clearedAt));
        else marker.MoveTo(clearedAt);

        // The watermark subsumes every hide behind it, so they are dropped rather than left to
        // accumulate for the life of the conversation.
        await db.ChatMessageHides
            .Where(x => x.UserId == userId && x.ConversationKey == key)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context, new ClearChatResponse(key, clearedAt), "Chat cleared for you.");
    }

    private static async Task<List<Guid>> HideAsync(IMirageDbContext db, Guid userId, ChatSurface chat,
        IReadOnlyCollection<Guid> messageIds, ChatSurfaceService surfaces, CancellationToken cancellationToken)
    {
        // Only ids that really are messages in this conversation are hidden, so a bad id cannot
        // plant rows that make an unrelated message vanish if it is ever moved here.
        var hidden = new List<Guid>();
        var already = await db.ChatMessageHides.AsNoTracking()
            .Where(x => x.UserId == userId && x.ConversationKey == chat.Key)
            .Select(x => x.MessageId)
            .ToListAsync(cancellationToken);
        var seen = already.ToHashSet();

        foreach (var messageId in messageIds)
        {
            if (!seen.Add(messageId)) continue;
            if (await surfaces.FindMessageAsync(chat, messageId, cancellationToken) is null) continue;
            db.ChatMessageHides.Add(new ChatMessageHide(userId, chat.Key, messageId));
            hidden.Add(messageId);
        }

        if (hidden.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return hidden;
    }

    // ------------------------------------------------------------------ shared

    private readonly record struct ResolvedSurface(ChatSurface Surface, IResult? Failure);

    private static async Task<ResolvedSurface> ResolveAsync(string surface, Guid id, HttpContext context,
        ChatSurfaceService surfaces, CancellationToken cancellationToken)
    {
        if (!ChatSurface.TryParse(surface, id, out var chat))
            return new ResolvedSurface(default!, EndpointHelpers.NotFound(context, "That conversation kind is unknown."));
        var userId = context.User.GetUserId();
        if (!await surfaces.IsParticipantAsync(chat, userId, cancellationToken))
            return new ResolvedSurface(chat, EndpointHelpers.Forbidden(context));
        return new ResolvedSurface(chat, null);
    }
}
