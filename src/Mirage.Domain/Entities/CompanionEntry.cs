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

    public void Update(string answerText)
    {
        AnswerText = answerText.Trim();
        Touch();
    }
}
