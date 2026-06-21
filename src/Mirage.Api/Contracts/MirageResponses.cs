using Mirage.Api.Domain;

namespace Mirage.Api.Contracts;

public sealed record PlatformOverviewResponse(
    string ProductName,
    string Mission,
    IReadOnlyList<string> ValuePropositions,
    IReadOnlyList<string> LaunchModules,
    IReadOnlyList<string> RoadmapPhases);

public sealed record CreateDateRequestRequest(
    Guid RequestorId,
    string Activity,
    string DateTimeWindow,
    string LocationArea,
    string? Note);

public sealed record CreateSessionRequest(
    Guid CounsellorId,
    Guid ClientId,
    SessionType Type,
    DateTimeOffset ScheduledAt,
    bool CounsellorAnonymous,
    bool ClientAnonymous,
    string Topic);
