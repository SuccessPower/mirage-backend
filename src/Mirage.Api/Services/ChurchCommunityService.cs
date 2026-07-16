using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Finds or creates a church's auto-managed community (General or Married, see
// Community.ChurchGeneralCategory/ChurchMarriedCategory) and adds a user to it. The org's owner
// and managers are seeded as Owner/Moderator the first time the community is created.
internal static class ChurchCommunityService
{
    public static async Task JoinChurchCommunityAsync(IMirageDbContext db, Guid organisationId, string category,
        Guid userId, CancellationToken cancellationToken)
    {
        var community = await db.Communities
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.Category == category, cancellationToken);

        if (community is null)
        {
            var org = await db.Organisations.AsNoTracking()
                .SingleAsync(x => x.Id == organisationId, cancellationToken);
            var description = category == Community.ChurchMarriedCategory
                ? $"Married couples at {org.Name}"
                : $"Members of {org.Name}";
            community = new Community(org.AdminUserId, org.Name, category, description, org.LogoUrl,
                organisationId: organisationId);
            db.Communities.Add(community);

            var seededUserIds = new HashSet<Guid> { org.AdminUserId };
            community.Members.Add(new CommunityMember(community.Id, org.AdminUserId, CommunityMemberRole.Owner));

            var managerIds = await db.OrganisationManagers.AsNoTracking()
                .Where(x => x.OrganisationId == organisationId)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);
            foreach (var managerId in managerIds.Where(seededUserIds.Add))
                community.Members.Add(new CommunityMember(community.Id, managerId, CommunityMemberRole.Moderator));
        }

        var alreadyMember = community.Members.Any(x => x.UserId == userId) ||
            await db.CommunityMembers.AnyAsync(x => x.CommunityId == community.Id && x.UserId == userId, cancellationToken);
        if (!alreadyMember)
            db.CommunityMembers.Add(new CommunityMember(community.Id, userId));
    }

    // Counterpart to JoinChurchCommunityAsync — called when a member leaves/is removed/rejected
    // from the organisation, so they don't linger in the church's auto-managed community after
    // losing their membership. Leaves the community's Owner alone even if asked, since that seat
    // always belongs to the org's own admin/owner, not a regular member record.
    public static async Task LeaveChurchCommunityAsync(IMirageDbContext db, Guid organisationId, string category,
        Guid userId, CancellationToken cancellationToken)
    {
        var community = await db.Communities
            .SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.Category == category, cancellationToken);
        if (community is null) return;

        var member = await db.CommunityMembers.SingleOrDefaultAsync(
            x => x.CommunityId == community.Id && x.UserId == userId && x.LeftAt == null, cancellationToken);
        if (member is null || member.Role == CommunityMemberRole.Owner) return;

        member.Leave();
    }
}
