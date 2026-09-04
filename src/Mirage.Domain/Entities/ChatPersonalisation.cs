using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

/// <summary>
/// One member's chosen wallpaper for one conversation, or their account-wide default when
/// <see cref="ConversationKey"/> is <see cref="AccountDefaultKey"/>.
/// </summary>
/// <remarks>
/// The theme itself (gradient, doodle, bubble tints) lives in the clients' shared catalogue — the
/// server stores only the key that was picked, so a new theme ships without a migration. Kept per
/// user rather than per conversation: a wallpaper is how one member wants their own screen to
/// look, and imposing it on whoever they are talking to would be someone else's decision.
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
