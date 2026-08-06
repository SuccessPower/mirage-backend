using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

// A user's journal answer to a CompanionPrompt. Visible to the author and (once approved) to
// their CompanionPartner.
public sealed class CompanionEntry : Entity
{
    private CompanionEntry() { }

    public CompanionEntry(Guid promptId, Guid authorUserId, string answerText)
    {
        PromptId = promptId;
        AuthorUserId = authorUserId;
        AnswerText = answerText.Trim();
    }

    public Guid PromptId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string AnswerText { get; private set; } = string.Empty;

    // Set the first time the linked CompanionPartner reads this entry, so the author can see
    // their answer landed. Never cleared — a re-read keeps the original timestamp.
    public DateTimeOffset? PartnerReadAt { get; private set; }

    public void Update(string answerText)
    {
        AnswerText = answerText.Trim();
        Touch();
    }

    public bool MarkReadByPartner(DateTimeOffset readAt)
    {
        if (PartnerReadAt is not null) return false;
        PartnerReadAt = readAt;
        return true;
    }
}
