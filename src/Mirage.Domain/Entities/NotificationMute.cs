using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// One member silencing one shared space — a community, a mentor or counsellor group, or a couple
// friendship. Notifications are on by default, so the table only holds the exceptions: a row means
// "stop telling me about activity here", and turning notifications back on deletes it. Direct
// signals aimed at the member (an @mention, a reply to their comment) are not affected — muting a
// space quiets its chatter, not people talking to you.
public sealed class NotificationMute : Entity
{
    private NotificationMute() { }

    public NotificationMute(Guid userId, NotificationScope scope, Guid scopeId)
    {
        UserId = userId;
        Scope = scope;
        ScopeId = scopeId;
    }

    public Guid UserId { get; private set; }
    public NotificationScope Scope { get; private set; }
    // The community id, mentor profile id, counsellor profile id or couple friendship id.
    public Guid ScopeId { get; private set; }
}
