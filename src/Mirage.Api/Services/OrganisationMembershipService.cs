using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Church selection at signup / profile completion / the "add your church" nudge honors the org's
// RequireApproval setting the same way OrganisationEndpoints.JoinOrganisation does: an Approved,
// open organisation (RequireApproval == false — the default) admits the member immediately and
// verifies their profile, while a gated or still-pending organisation leaves the row Pending for
// the ChurchAdmin to review.
internal static class OrganisationMembershipService
{
    public static async Task<OrganisationMember> AddMemberAsync(IMirageDbContext db, Guid organisationId,
        Guid userId, Guid? branchId, UserProfile? profile, CancellationToken cancellationToken)
    {
        var member = new OrganisationMember(organisationId, userId, branchId);

        // A freshly proposed church isn't in the database yet (and is Pending anyway), so this
        // simply resolves to "not open" for that case.
        var isOpen = await db.Organisations.AsNoTracking().AnyAsync(
            x => x.Id == organisationId && x.Status == OrganisationStatus.Approved && !x.RequireApproval,
            cancellationToken);
        if (isOpen)
        {
            member.Approve();
            if (profile is not null && !profile.IsVerified) profile.Verify();
        }

        db.OrganisationMembers.Add(member);
        return member;
    }
}
