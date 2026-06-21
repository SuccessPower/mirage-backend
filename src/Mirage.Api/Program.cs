using Mirage.Api.Contracts;
using Mirage.Api.Domain;
using Mirage.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MirageSeedStore>();
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy("MirageFrontend", policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("MirageFrontend");

var api = app.MapGroup("/api").WithTags("Mirage");

api.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mirage-api" }));

api.MapGet("/platform/overview", () => new PlatformOverviewResponse(
    "Mirage",
    "To empower individuals to build meaningful, lasting relationships grounded in community, mentorship, and faith.",
    [
        "Friendship to marriage lifecycle support",
        "Church-vetted counsellors and mentors",
        "Anonymous counselling by default",
        "Open date request marketplace",
        "Marriage companion tools after the wedding"
    ],
    [
        "Onboarding and profiles",
        "Community discovery",
        "Dating and open date requests",
        "Counselling and mentorship",
        "Marriage companion"
    ],
    [
        "Foundation",
        "Community and discovery",
        "Dating features",
        "Counselling MVP",
        "Marriage companion",
        "Payments and scale"
    ]));

api.MapGet("/profiles", (MirageSeedStore store, RelationshipIntent? intent, string? city) =>
{
    var profiles = store.Profiles.AsEnumerable();

    if (intent is not null)
    {
        profiles = profiles.Where(profile => profile.Intent == intent);
    }

    if (!string.IsNullOrWhiteSpace(city))
    {
        profiles = profiles.Where(profile => profile.City.Contains(city, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(profiles);
});

api.MapGet("/date-requests", (MirageSeedStore store) => Results.Ok(store.DateRequests));

api.MapPost("/date-requests", (CreateDateRequestRequest request, MirageSeedStore store) =>
{
    if (request.RequestorId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.Activity) ||
        string.IsNullOrWhiteSpace(request.DateTimeWindow) ||
        string.IsNullOrWhiteSpace(request.LocationArea))
    {
        return Results.BadRequest(new { error = "Requestor, activity, date/time window, and location are required." });
    }

    var created = store.CreateDateRequest(request);
    return Results.Created($"/api/date-requests/{created.Id}", created);
});

api.MapGet("/counsellors", (MirageSeedStore store, string? specialisation, bool? freeOnly) =>
{
    var counsellors = store.Counsellors.Where(counsellor => counsellor.IsApproved);

    if (!string.IsNullOrWhiteSpace(specialisation))
    {
        counsellors = counsellors.Where(counsellor =>
            counsellor.Specialisations.Any(item => item.Contains(specialisation, StringComparison.OrdinalIgnoreCase)));
    }

    if (freeOnly == true)
    {
        var freeOrgNames = store.Organisations.Where(org => org.OffersFreeSessions).Select(org => org.Name).ToHashSet();
        counsellors = counsellors.Where(counsellor => freeOrgNames.Contains(counsellor.Organisation));
    }

    return Results.Ok(counsellors);
});

api.MapGet("/organisations", (MirageSeedStore store) => Results.Ok(store.Organisations));

api.MapGet("/sessions", (MirageSeedStore store) => Results.Ok(store.Sessions));

api.MapPost("/sessions", (CreateSessionRequest request, MirageSeedStore store) =>
{
    if (request.CounsellorId == Guid.Empty ||
        request.ClientId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.Topic))
    {
        return Results.BadRequest(new { error = "Counsellor, client, and topic are required." });
    }

    var created = store.CreateSession(request);
    return Results.Created($"/api/sessions/{created.Id}", created);
});

api.MapGet("/companion/check-ins", (MirageSeedStore store) => Results.Ok(store.CompanionCheckIns));

app.Run();
