using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class SessionNote : Entity
{
    private SessionNote() { }

    public SessionNote(Guid sessionId, Guid authorUserId, string content)
    {
        SessionId = sessionId;
        AuthorUserId = authorUserId;
        Content = content.Trim();
    }

    public Guid SessionId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Content { get; private set; } = string.Empty;

    public void Update(string content) { Content = content.Trim(); Touch(); }
}

public sealed class SessionRating : Entity
{
    private SessionRating() { }

    public SessionRating(Guid sessionId, Guid reviewerUserId, int rating, string? comment)
    {
        SessionId = sessionId;
        ReviewerUserId = reviewerUserId;
        Rating = Math.Clamp(rating, 1, 5);
        Comment = comment?.Trim();
    }

    public Guid SessionId { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }
}
