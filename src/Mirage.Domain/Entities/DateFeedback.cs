using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class DateFeedback : Entity
{
    private DateFeedback() { }

    public DateFeedback(Guid dateRequestId, Guid reviewerUserId, Guid reviewedUserId, int rating, string? comment)
    {
        DateRequestId = dateRequestId;
        ReviewerUserId = reviewerUserId;
        ReviewedUserId = reviewedUserId;
        Rating = Math.Clamp(rating, 1, 5);
        Comment = comment?.Trim();
    }

    public Guid DateRequestId { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public Guid ReviewedUserId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }
}
