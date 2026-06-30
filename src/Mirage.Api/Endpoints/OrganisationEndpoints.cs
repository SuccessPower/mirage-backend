using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

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

        // PlatformAdmin: review pending orgs
        organisations.MapGet("/pending", ListPending).RequireAuthorization(MiragePolicy.PlatformAdmin);
        organisations.MapPatch("/{id:guid}/approve", ApproveOrg).RequireAuthorization(MiragePolicy.PlatformAdmin);
        organisations.MapPatch("/{id:guid}/reject", RejectOrg).RequireAuthorization(MiragePolicy.PlatformAdmin);

        // ChurchAdmin: manage counsellors within their org
        organisations.MapGet("/{id:guid}/counsellors", ListCounsellors).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapPost("/{id:guid}/counsellors/invite", InviteCounsellor).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapPatch("/{id:guid}/counsellors/{counsellorId:guid}/approve", ApproveCounsellor).RequireAuthorization(MiragePolicy.ChurchAdmin);

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

    // --- PlatformAdmin: organisation approval ---

    private static async Task<IResult> ListPending(HttpContext context, IMirageDbContext db, CancellationToken ct)
    {
        var orgs = await db.Organisations.AsNoTracking()
            .Where(x => x.Status == OrganisationStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Name, x.Denomination, x.Country, x.RegistrationNumber, x.AdminUserId, x.CreatedAt })
            .ToListAsync(ct);
        return ApiResults.Ok(context, orgs, "Pending organisations retrieved successfully.");
    }

    private static async Task<IResult> ApproveOrg(Guid id, HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var org = await db.Organisations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.Status == OrganisationStatus.Approved)
            return EndpointHelpers.Conflict(context, "Organisation is already approved.");

        var admin = await userManager.FindByIdAsync(org.AdminUserId.ToString());
        if (admin is null) return EndpointHelpers.NotFound(context, "Organisation admin user was not found.");

        org.Approve();
        await db.SaveChangesAsync(cancellationToken);

        if (!await userManager.IsInRoleAsync(admin, MirageRoles.ChurchAdmin))
            await userManager.AddToRoleAsync(admin, MirageRoles.ChurchAdmin);

        return ApiResults.Ok(context, new { org.Id, org.Status }, "Organisation approved and admin granted ChurchAdmin role.");
    }

    private static async Task<IResult> RejectOrg(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var org = await db.Organisations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.Status is OrganisationStatus.Rejected or OrganisationStatus.Suspended)
            return EndpointHelpers.Conflict(context, $"Organisation is already {org.Status.ToString().ToLower()}.");
        org.Reject();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { org.Id, org.Status }, "Organisation rejected.");
    }

    // --- ChurchAdmin: counsellor management ---

    private static async Task<IResult> ListCounsellors(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var org = await db.Organisations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.AdminUserId != userId && !context.User.IsInRole(MirageRoles.PlatformAdmin))
            return EndpointHelpers.Forbidden(context);

        var counsellors = await db.Counsellors.AsNoTracking()
            .Where(x => x.OrganisationId == id)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.YearsExperience,
                x.IsApproved,
                x.IsAnonymous,
                x.AcceptsFreeSessions,
                x.Specialisations,
                x.Languages,
                DisplayName = x.UserProfile.DisplayName
            })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, counsellors, "Counsellors retrieved successfully.");
    }

    private static async Task<IResult> InviteCounsellor(Guid id, InviteCounsellorRequest request,
        HttpContext context, IMirageDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "A valid email address is required."));

        var userId = context.User.GetUserId();
        var org = await db.Organisations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.AdminUserId != userId && !context.User.IsInRole(MirageRoles.PlatformAdmin))
            return EndpointHelpers.Forbidden(context);
        if (org.Status != OrganisationStatus.Approved)
            return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Organisation not active", "Only approved organisations can invite counsellors.");

        var normalizedEmail = request.Email.ToLowerInvariant().Trim();
        var existingPending = await db.CounsellorInvites.AnyAsync(
            x => x.OrganisationId == id && x.Email == normalizedEmail && x.RedeemedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow,
            cancellationToken);
        if (existingPending)
            return EndpointHelpers.Conflict(context, "An active invite already exists for this email address.");

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var expiryDays = configuration.GetValue("CounsellorInvite:ExpiryDays", 7);
        db.CounsellorInvites.Add(new CounsellorInvite(id, normalizedEmail, rawToken, DateTimeOffset.UtcNow.AddDays(expiryDays)));
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context,
            new { Email = normalizedEmail, InviteToken = rawToken, ExpiresInDays = expiryDays },
            "Counsellor invite created. Share the invite token with the counsellor to complete registration.");
    }

    private static async Task<IResult> ApproveCounsellor(Guid id, Guid counsellorId, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var org = await db.Organisations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.AdminUserId != userId && !context.User.IsInRole(MirageRoles.PlatformAdmin))
            return EndpointHelpers.Forbidden(context);

        var counsellor = await db.Counsellors
            .SingleOrDefaultAsync(x => x.Id == counsellorId && x.OrganisationId == id, cancellationToken);
        if (counsellor is null) return EndpointHelpers.NotFound(context, "Counsellor was not found in this organisation.");
        if (counsellor.IsApproved)
            return EndpointHelpers.Conflict(context, "Counsellor is already approved.");

        counsellor.Approve();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { counsellor.Id, counsellor.IsApproved }, "Counsellor approved successfully.");
    }
}
