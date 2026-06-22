using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class OrganisationEndpoints
{
    public static RouteGroupBuilder MapOrganisationEndpoints(this RouteGroupBuilder api)
    {
        var organisations = api.MapGroup("/organisations").WithTags("Organisations");
        organisations.MapGet("/", async (HttpContext context, IMirageDbContext db, CancellationToken ct) =>
            ApiResults.Ok(context,
                await db.Organisations.AsNoTracking().Where(x => x.Status == OrganisationStatus.Approved)
                    .OrderBy(x => x.Name).ToListAsync(ct),
                "Organisations retrieved successfully."));
        organisations.MapPost("/", Create).RequireAuthorization();

        var recommendations = api.MapGroup("/recommendations").WithTags("Recommendations").RequireAuthorization();
        recommendations.MapGet("/", ListRecommendations);
        recommendations.MapPost("/", Recommend);
        recommendations.MapDelete("/{id:guid}", RevokeRecommendation);
        return api;
    }

    private static async Task<IResult> Create(CreateOrganisationRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.RegistrationNumber))
            return EndpointHelpers.ValidationProblem(context,
                ("organisation", "Name and registration number are required."));
        var organisation = new Organisation(context.User.GetUserId(), request.Name, request.Denomination,
            request.Country, request.RegistrationNumber);
        db.Organisations.Add(organisation);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/organisations/{organisation.Id}",
            new { organisation.Id, organisation.Status }, "Organisation submitted successfully.");
    }

    private static async Task<IResult> Recommend(CreateRecommendationRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var actor = context.User.GetUserId();
        if (actor == request.RecommendedUserId)
            return EndpointHelpers.ValidationProblem(context,
                ("recommendedUserId", "A user cannot recommend themselves."));
        if (!await db.Profiles.AnyAsync(x => x.UserId == request.RecommendedUserId, cancellationToken))
            return EndpointHelpers.NotFound(context, "Recommended user was not found.");
        if (await db.Recommendations.AnyAsync(x => x.RecommendedUserId == request.RecommendedUserId &&
                                                  x.RecommendedByUserId == actor, cancellationToken))
            return EndpointHelpers.Conflict(context, "This recommendation already exists.");
        var recommendation = new Recommendation(request.RecommendedUserId, actor, request.OrganisationId, request.Note);
        db.Recommendations.Add(recommendation);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/recommendations/{recommendation.Id}",
            new { recommendation.Id }, "Recommendation created successfully.");
    }

    private static async Task<IResult> ListRecommendations(HttpContext context, IMirageDbContext db,
        string direction = "received", int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var query = db.Recommendations.AsNoTracking().AsQueryable();
        query = direction.Equals("given", StringComparison.OrdinalIgnoreCase)
            ? query.Where(x => x.RecommendedByUserId == userId)
            : query.Where(x => x.RecommendedUserId == userId);
        var result = query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id,
            x.RecommendedUserId,
            x.RecommendedByUserId,
            x.OrganisationId,
            OrganisationName = x.Organisation != null ? x.Organisation.Name : null,
            x.Note,
            x.Status,
            x.CreatedAt
        });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Recommendations retrieved successfully.");
    }

    private static async Task<IResult> RevokeRecommendation(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var recommendation = await db.Recommendations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (recommendation is null) return EndpointHelpers.NotFound(context, "Recommendation was not found.");
        if (recommendation.RecommendedByUserId != context.User.GetUserId())
            return EndpointHelpers.Forbidden(context);
        if (recommendation.Status == RecommendationStatus.Revoked)
            return EndpointHelpers.Conflict(context, "Recommendation is already revoked.");
        recommendation.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { recommendation.Id }, "Recommendation revoked successfully.");
    }
}
