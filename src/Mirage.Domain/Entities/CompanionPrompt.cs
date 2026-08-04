using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A reflective/heartfelt journal question in the Companion question bank. Reference data, not
// tied to any one user — seeded on startup, drawn at random per-cadence by CompanionReminderService.
public sealed class CompanionPrompt : Entity
{
    private CompanionPrompt() { }

    public CompanionPrompt(string text, string category, CompanionCadence cadence)
    {
        Text = text.Trim();
        Category = category.Trim();
        Cadence = cadence;
        IsActive = true;
    }

    public string Text { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public CompanionCadence Cadence { get; private set; }
    public bool IsActive { get; private set; }
}
