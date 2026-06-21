using Mirage.Api.Contracts;
using Mirage.Api.Domain;

namespace Mirage.Api.Infrastructure;

public sealed class MirageSeedStore
{
    private readonly List<DateRequest> _dateRequests;
    private readonly List<CounsellingSession> _sessions;

    public MirageSeedStore()
    {
        Profiles =
        [
            new Profile(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Tolu Adebayo", 27, "Lagos", "Nigeria", "Pentecostal", RelationshipIntent.Dating, true, "Grace Chapel", "Christian nurse seeking intentional friendship and a serious relationship.", ["music", "service", "fitness"]),
            new Profile(Guid.Parse("22222222-2222-2222-2222-222222222222"), "David Okafor", 31, "Abuja", "Nigeria", "Anglican", RelationshipIntent.Dating, true, "Community verified", "New to the city and interested in low-pressure, values-led dates.", ["coffee", "books", "volunteering"]),
            new Profile(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Adaeze Nwosu", 55, "Enugu", "Nigeria", "Catholic", RelationshipIntent.Marriage, true, "Mirage Mentor Network", "Marriage mentor with 28 years of experience supporting couples.", ["mentorship", "family", "faith"])
        ];

        Counsellors =
        [
            new Counsellor(Guid.Parse("44444444-4444-4444-4444-444444444444"), "M***a A***e", "Pentecostal", "Grace Chapel", 12, true, true, ["premarital counselling", "communication", "conflict repair"], ["English", "Yoruba"]),
            new Counsellor(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Pastor James Bello", "Baptist", "New City Church", 18, false, true, ["marriage preparation", "family systems", "faith"], ["English", "Hausa"])
        ];

        Organisations =
        [
            new Organisation(Guid.Parse("66666666-6666-6666-6666-666666666666"), "Grace Chapel", "Pentecostal", "Nigeria", true, 8, [SessionType.Group, SessionType.Personal], true),
            new Organisation(Guid.Parse("77777777-7777-7777-7777-777777777777"), "New City Church", "Baptist", "Nigeria", true, 4, [SessionType.Personal], false)
        ];

        _dateRequests =
        [
            new DateRequest(Guid.Parse("88888888-8888-8888-8888-888888888888"), Profiles[1].Id, "Coffee after Sunday service", "Saturday, 4-7 PM", "Wuse 2, Abuja", "Simple first conversation in a public place.", DateRequestStatus.Open, 6)
        ];

        _sessions =
        [
            new CounsellingSession(Guid.Parse("99999999-9999-9999-9999-999999999999"), Counsellors[0].Id, Profiles[0].Id, SessionType.Personal, DateTimeOffset.UtcNow.AddDays(3), SessionStatus.Scheduled, true, true, "Preparing for intentional dating")
        ];
    }

    public IReadOnlyList<Profile> Profiles { get; }

    public IReadOnlyList<Counsellor> Counsellors { get; }

    public IReadOnlyList<Organisation> Organisations { get; }

    public IReadOnlyList<DateRequest> DateRequests => _dateRequests;

    public IReadOnlyList<CounsellingSession> Sessions => _sessions;

    public IReadOnlyList<CompanionCheckIn> CompanionCheckIns { get; } =
    [
        new CompanionCheckIn(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "What made you feel seen by your partner this week?", "Weekly", "Connection"),
        new CompanionCheckIn(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Where do we need more honesty, tenderness, or structure?", "Bi-weekly", "Communication"),
        new CompanionCheckIn(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Which shared goal should we revisit before the month ends?", "Monthly", "Planning")
    ];

    public DateRequest CreateDateRequest(CreateDateRequestRequest request)
    {
        var created = new DateRequest(
            Guid.NewGuid(),
            request.RequestorId,
            request.Activity.Trim(),
            request.DateTimeWindow.Trim(),
            request.LocationArea.Trim(),
            request.Note?.Trim() ?? string.Empty,
            DateRequestStatus.Open,
            CompatibleAcceptors: 0);

        _dateRequests.Add(created);
        return created;
    }

    public CounsellingSession CreateSession(CreateSessionRequest request)
    {
        var created = new CounsellingSession(
            Guid.NewGuid(),
            request.CounsellorId,
            request.ClientId,
            request.Type,
            request.ScheduledAt,
            SessionStatus.Requested,
            request.CounsellorAnonymous,
            request.ClientAnonymous,
            request.Topic.Trim());

        _sessions.Add(created);
        return created;
    }
}
