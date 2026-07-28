using Microsoft.EntityFrameworkCore;
using Mirage.Api.Endpoints;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Domain.Services;

namespace Mirage.Api.Services;

// Resolves a church picked at signup (or later, via the "add your church" nudge / profile
// completion flow), or provisions a new one/branch when it wasn't found in search — same
// "submitted for review" semantics as OrganisationEndpoints.Create, just inlined so picking or
// proposing a church is one step. Shared by AuthEndpoints.Register and ProfileEndpoints.
internal static class ChurchSelectionResolver
{
    public static async Task<(Guid? OrganisationId, Guid? BranchId, IResult? Error)> ResolveAsync(
        Guid actingUserId, string denomination, string country,
        Guid? organisationId, Guid? branchId,
        string? newOrganisationName, string? newOrganisationRegistrationNumber,
        string? newBranchName, string? newBranchCity,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(newOrganisationName))
        {
            var existingOrganisations = await db.Organisations.AsNoTracking()
                .Select(x => new { x.Id, x.Name, x.Country, x.WebsiteUrl, x.Status })
                .ToListAsync(cancellationToken);
            var duplicate = existingOrganisations.FirstOrDefault(x =>
                OrganisationIdentity.IsLikelyDuplicate(newOrganisationName, country, null,
                    x.Name, x.Country, x.WebsiteUrl));
            if (duplicate is not null)
            {
                if (duplicate.Status == OrganisationStatus.Approved)
                    return (duplicate.Id, null, null);

                return (null, null, EndpointHelpers.Conflict(context,
                    $"“{duplicate.Name}” is already listed and awaiting review."));
            }

            var registrationNumber = string.IsNullOrWhiteSpace(newOrganisationRegistrationNumber)
                ? $"PENDING-{Guid.NewGuid():N}"
                : newOrganisationRegistrationNumber.Trim();
            var organisation = new Organisation(actingUserId, newOrganisationName, denomination, country,
                registrationNumber);
            db.Organisations.Add(organisation);

            Guid? createdBranchId = null;
            if (!string.IsNullOrWhiteSpace(newBranchName) && !string.IsNullOrWhiteSpace(newBranchCity))
            {
                var branch = new OrganisationBranch(organisation.Id, newBranchName, newBranchCity, country, null);
                db.OrganisationBranches.Add(branch);
                createdBranchId = branch.Id;
            }
            return (organisation.Id, createdBranchId, null);
        }

        if (organisationId is null) return (null, null, null);

        var org = await db.Organisations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == organisationId, cancellationToken);
        if (org is null)
            return (null, null, EndpointHelpers.ValidationProblem(context, ("organisationId", "Church was not found.")));
        if (org.Status != OrganisationStatus.Approved)
            return (null, null, EndpointHelpers.ValidationProblem(context, ("organisationId", "This church is not yet active.")));

        var resolvedBranchId = branchId;
        if (resolvedBranchId.HasValue)
        {
            var branchBelongs = await db.OrganisationBranches.AsNoTracking()
                .AnyAsync(x => x.Id == resolvedBranchId && x.OrganisationId == org.Id, cancellationToken);
            if (!branchBelongs)
                return (null, null, EndpointHelpers.ValidationProblem(context, ("branchId", "Branch does not belong to this church.")));
        }
        else if (!string.IsNullOrWhiteSpace(newBranchName) && !string.IsNullOrWhiteSpace(newBranchCity))
        {
            var branch = new OrganisationBranch(org.Id, newBranchName, newBranchCity, org.Country, null);
            db.OrganisationBranches.Add(branch);
            resolvedBranchId = branch.Id;
        }

        return (org.Id, resolvedBranchId, null);
    }
}
