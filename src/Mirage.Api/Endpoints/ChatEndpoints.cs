using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Hubs;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

// Everything that is true of a conversation whichever conversation it is: the wallpaper a member
// picked for it, the messages they deleted from their own copy, the history they cleared, and
// taking a message back from everyone inside the five-minute window.
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

        // Deletion.
        chats.MapPost("/{surface}/{id:guid}/messages/delete", DeleteMessages);
        chats.MapPost("/{surface}/{id:guid}/clear", ClearConversation);

        return chats;
    }

    // ------------------------------------------------------------------ wallpapers

    private static async Task<IResult> GetThemes(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var rows = await db.ChatThemePreferences.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.ConversationKey, x.ThemeKey })
            .ToListAsync(cancellationToken);

        var defaultTheme = rows
            .SingleOrDefault(x => x.ConversationKey == ChatThemePreference.AccountDefaultKey)?.ThemeKey;
        var overrides = rows
            .Where(x => x.ConversationKey != ChatThemePreference.AccountDefaultKey)
            .Select(x => new ChatThemeOverrideResponse(x.ConversationKey, x.ThemeKey))
            .ToList();

        return ApiResults.Ok(context, new ChatThemesResponse(defaultTheme, overrides),
            "Chat themes retrieved successfully.");
    }

    private static Task<IResult> SetDefaultTheme(SetChatThemeRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken) =>
        UpsertThemeAsync(ChatThemePreference.AccountDefaultKey, request, context, db, cancellationToken);

    private static async Task<IResult> SetConversationTheme(string surface, Guid id, SetChatThemeRequest request,
        HttpContext context, IMirageDbContext db, ChatSurfaceService surfaces, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;
        return await UpsertThemeAsync(resolved.Surface.Key, request, context, db, cancellationToken);
    }

    private static async Task<IResult> ClearConversationTheme(string surface, Guid id, HttpContext context,
        IMirageDbContext db, ChatSurfaceService surfaces, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(surface, id, context, surfaces, cancellationToken);
        if (resolved.Failure is { } failure) return failure;

        var userId = context.User.GetUserId();
        var key = resolved.Surface.Key;
        var existing = await db.ChatThemePreferences
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ConversationKey == key, cancellationToken);
        if (existing is not null)
        {
            db.ChatThemePreferences.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Falling back to the account default is the whole point of clearing an override, so the
        // resolved theme comes back rather than an empty body the client has to interpret.
        var defaultTheme = await db.ChatThemePreferences.AsNoTracking()
            .Where(x => x.UserId == userId && x.ConversationKey == ChatThemePreference.AccountDefaultKey)
            .Select(x => x.ThemeKey)
            .SingleOrDefaultAsync(cancellationToken);
        return ApiResults.Ok(context, new ChatThemeOverrideResponse(key, defaultTheme),
            "Chat theme reset successfully.");
    }

    private static async Task<IResult> UpsertThemeAsync(string conversationKey, SetChatThemeRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var themeKey = request.Theme?.Trim();
        if (string.IsNullOrWhiteSpace(themeKey))
            return EndpointHelpers.ValidationProblem(context, ("theme", "A theme is required."));
        if (themeKey.Length > 60)
            return EndpointHelpers.ValidationProblem(context, ("theme", "Theme keys must be 60 characters or fewer."));

        var userId = context.User.GetUserId();
        var existing = await db.ChatThemePreferences
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ConversationKey == conversationKey, cancellationToken);
        if (existing is null)
            db.ChatThemePreferences.Add(new ChatThemePreference(userId, conversationKey, themeKey));
        else
            existing.SetTheme(themeKey);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context, new ChatThemeOverrideResponse(conversationKey, themeKey),
            "Chat theme saved successfully.");
    }

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
