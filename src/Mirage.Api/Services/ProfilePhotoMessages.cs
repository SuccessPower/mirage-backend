namespace Mirage.Api.Services;

// The photo-reminder wording is sent from two places (the on-demand nudge in GET /profiles/me and
// the background sweep), and it has to say the same thing in both — a member who gets one version
// in-app and another by email has no idea which rule applies. Kept here so the two can't drift.
public static class ProfilePhotoMessages
{
    public const string ReminderTitle = "Add two photos to unlock Mirage";

    // Says why we ask and what a bad photo costs. People comply with a rule they understand, and
    // "your photo was rejected" lands very differently when they were told the standard up front.
    public const string ReminderBody =
        "Mirage asks for at least two clear photos of you so members can see they're talking to a real person — "
        + "it's the main thing that keeps the community trustworthy. Until you add them, your profile stays hidden "
        + "and you can view only two profiles. Photos that don't clearly show your face — group shots, cartoons, "
        + "logos or screenshots — will be rejected, and your profile stays hidden until you replace them.";

    public const string CompleteTitle = "Your photos are approved — you're fully unlocked";

    public const string CompleteBody =
        "Your photos passed our checks, so your profile is now visible to everyone on Mirage and you can browse as "
        + "many profiles as you like. No more preview limit.";
}
