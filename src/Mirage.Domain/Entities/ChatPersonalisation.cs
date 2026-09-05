using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

/// <summary>
/// One member's account-wide default wallpaper: what a conversation wears when nobody in it has
/// chosen one.
/// </summary>
/// <remarks>
/// The theme itself (gradient, doodle, bubble tints) lives in the clients' shared catalogue — the
/// server stores only the key that was picked, so a new theme ships without a migration. This is
/// the private half of the picture: a member's default is theirs alone, while a wallpaper chosen
/// inside a conversation is shared with everyone in it and lives in
/// <see cref="ChatConversationTheme"/>.
/// </remarks>
public sealed class ChatThemePreference : Entity
{
    /// <summary>The key that stands for "every conversation without an override of its own".</summary>
    public const string AccountDefaultKey = "*";

    private ChatThemePreference() { }

    public ChatThemePreference(Guid userId, string conversationKey, string themeKey)
    {
        UserId = userId;
        ConversationKey = conversationKey;
        ThemeKey = themeKey;
    }

    public Guid UserId { get; private set; }
    public string ConversationKey { get; private set; } = string.Empty;
    public string ThemeKey { get; private set; } = string.Empty;

    public void SetTheme(string themeKey)
    {
        ThemeKey = themeKey;
        Touch();
    }
}

/// <summary>
/// A message one member has deleted from their own copy of a conversation ("delete for me").
/// </summary>
/// <remarks>
/// A hide rather than a delete: the row still belongs to the other participants, who never learn
/// that this member cleared it. Message ids are GUIDs and unique across every message table, so
/// one table serves every chat surface; <see cref="ConversationKey"/> is carried so the list
/// queries can filter with a single indexed lookup per conversation.
/// </remarks>
public sealed class ChatMessageHide : Entity
{
    private ChatMessageHide() { }

    public ChatMessageHide(Guid userId, string conversationKey, Guid messageId)
    {
        UserId = userId;
        ConversationKey = conversationKey;
        MessageId = messageId;
    }

    public Guid UserId { get; private set; }
    public string ConversationKey { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
}

/// <summary>
/// Where one member cleared a conversation: everything sent at or before <see cref="ClearedAt"/>
/// is hidden from them, and from them only.
/// </summary>
/// <remarks>
/// A watermark rather than a hide per message, so clearing a five-thousand-message history is one
/// row and stays one row as the conversation grows.
/// </remarks>
public sealed class ChatClearMarker : Entity
{
    private ChatClearMarker() { }

    public ChatClearMarker(Guid userId, string conversationKey, DateTimeOffset clearedAt)
    {
        UserId = userId;
        ConversationKey = conversationKey;
        ClearedAt = clearedAt;
    }

    public Guid UserId { get; private set; }
    public string ConversationKey { get; private set; } = string.Empty;
    public DateTimeOffset ClearedAt { get; private set; }

    public void MoveTo(DateTimeOffset clearedAt)
    {
        if (clearedAt > ClearedAt) ClearedAt = clearedAt;
        Touch();
    }
}

/// <summary>
/// The wallpaper a conversation wears, for everyone in it.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ChatThemePreference"/>, which is now only ever an account-wide
/// default: choosing a wallpaper *inside* a conversation is a shared act, the way naming a group
/// is, so the row belongs to the conversation rather than to the member who picked it. The hub
/// pushes the change to whoever else is in the thread, so their screen repaints without a reload.
/// Conversations with no row here fall back to each member's own account default, which stays
/// private.
/// </remarks>
public sealed class ChatConversationTheme : Entity
{
    private ChatConversationTheme() { }

    public ChatConversationTheme(string conversationKey, string themeKey, Guid setByUserId)
    {
        ConversationKey = conversationKey;
        ThemeKey = themeKey;
        SetByUserId = setByUserId;
    }

    public string ConversationKey { get; private set; } = string.Empty;
    public string ThemeKey { get; private set; } = string.Empty;

    /// <summary>Who chose it last — the clients name them when announcing the change.</summary>
    public Guid SetByUserId { get; private set; }

    public void SetTheme(string themeKey, Guid setByUserId)
    {
        ThemeKey = themeKey;
        SetByUserId = setByUserId;
        Touch();
    }
}

/// <summary>
/// One member's emoji reaction to one message, in whichever conversation it lives.
/// </summary>
/// <remarks>
/// One reaction per member per message: reacting again replaces what was there, the way it does
/// in every chat client people already use, so a message cannot collect a wall of emoji from a
/// single person. Message ids are GUIDs and unique across every message table, so — like
/// <see cref="ChatMessageHide"/> — one table serves all six chat surfaces, with
/// <see cref="ConversationKey"/> carried so a thread's reactions load in one indexed lookup.
///
/// Reactions are deliberately stored in the clear even where the messages themselves are
/// end-to-end encrypted: an emoji says nothing on its own without the message it hangs off, and
/// encrypting it would mean a reaction could not be counted without every reader holding a key.
/// </remarks>
public sealed class ChatMessageReaction : Entity
{
    /// <summary>Long enough for a ZWJ sequence with skin-tone modifiers, short enough to not be a message.</summary>
    public const int MaxEmojiLength = 24;

    private ChatMessageReaction() { }

    public ChatMessageReaction(Guid userId, string conversationKey, Guid messageId, string emoji)
    {
        UserId = userId;
        ConversationKey = conversationKey;
        MessageId = messageId;
        Emoji = emoji;
    }

    public Guid UserId { get; private set; }
    public string ConversationKey { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
    public string Emoji { get; private set; } = string.Empty;

    public void SetEmoji(string emoji)
    {
        Emoji = emoji;
        Touch();
    }
}
