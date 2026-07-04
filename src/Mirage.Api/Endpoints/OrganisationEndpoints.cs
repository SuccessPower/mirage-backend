using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
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
                await db.Organisations.AsNoTracking()
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

        // Membership: users request to join, ChurchAdmin approves/rejects/removes/assigns
        organisations.MapGet("/mine", ListMyMemberships).RequireAuthorization();
        organisations.MapPost("/{id:guid}/join", JoinOrganisation).RequireAuthorization();
        organisations.MapGet("/{id:guid}/members", ListMembers).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapPatch("/{id:guid}/members/{memberId:guid}/approve", ApproveMember).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapPatch("/{id:guid}/members/{memberId:guid}/reject", RejectMember).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapDelete("/{id:guid}/members/{memberId:guid}", RemoveMember).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapPatch("/{id:guid}/members/{memberId:guid}/assign", AssignMember).RequireAuthorization(MiragePolicy.ChurchAdmin);

        // Branches
        organisations.MapGet("/{id:guid}/branches", ListBranches);
        organisations.MapPost("/{id:guid}/branches", CreateBranch).RequireAuthorization(MiragePolicy.ChurchAdmin);

        // Events + tickets
        organisations.MapGet("/{id:guid}/events", ListEvents);
        organisations.MapPost("/{id:guid}/events", CreateEvent).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapPost("/{id:guid}/events/{eventId:guid}/register", RegisterForEvent).RequireAuthorization();
        organisations.MapGet("/{id:guid}/events/{eventId:guid}/tickets", ListEventTickets).RequireAuthorization(MiragePolicy.ChurchAdmin);
        organisations.MapGet("/tickets/mine", ListMyTickets).RequireAuthorization();

        var recommendations = api.MapGroup("/recommendations").WithTags("Recommendations").RequireAuthorization();
        recommendations.MapGet("/", ListRecommendations);
        recommendations.MapPost("/", Recommend);
        recommendations.MapDelete("/{id:guid}", RevokeRecommendation);
        return api;
    }

    private static async Task<IResult> Create(CreateOrganisationRequest request, HttpContext context,
        MirageDbContext db, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.RegistrationNumber))
            return EndpointHelpers.ValidationProblem(context,
                ("organisation", "Name and registration number are required."));

        var userId = context.User.GetUserId();
        var organisation = new Organisation(userId, request.Name, request.Denomination,
            request.Country, request.RegistrationNumber);

        // A PlatformAdmin-issued invite lets this org skip the Pending review queue entirely.
        if (!string.IsNullOrWhiteSpace(request.InviteToken))
        {
            var tokenHash = OrganisationAdminInvite.ComputeHash(request.InviteToken);
            var invite = await db.OrganisationAdminInvites.SingleOrDefaultAsync(
                x => x.TokenHash == tokenHash, cancellationToken);
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (invite is null || !invite.IsValid || user is null ||
                !invite.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase))
                return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                    "Invalid invite", "This invite token is invalid, expired, or does not match your account email.");

            organisation.Approve();
            invite.Redeem();
            db.Organisations.Add(organisation);
            await db.SaveChangesAsync(cancellationToken);

            if (!await userManager.IsInRoleAsync(user, MirageRoles.ChurchAdmin))
                await userManager.AddToRoleAsync(user, MirageRoles.ChurchAdmin);

            return ApiResults.Created(context, $"/api/v1/organisations/{organisation.Id}",
                new { organisation.Id, organisation.Status },
                "Organisation created and approved successfully. You are now a ChurchAdmin.");
        }

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
        UserManager<ApplicationUser> userManager, NotificationService notifications, CancellationToken cancellationToken)
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

        await notifications.NotifyAsync(org.AdminUserId, NotificationType.OrganisationApproved,
            "Organisation approved", $"{org.Name} has been approved. You are now a ChurchAdmin.",
            org.Id, "Organisation", cancellationToken);

        return ApiResults.Ok(context, new { org.Id, org.Status }, "Organisation approved and admin granted ChurchAdmin role.");
    }

    private static async Task<IResult> RejectOrg(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var org = await db.Organisations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.Status is OrganisationStatus.Rejected or OrganisationStatus.Suspended)
            return EndpointHelpers.Conflict(context, $"Organisation is already {org.Status.ToString().ToLower()}.");
        org.Reject();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(org.AdminUserId, NotificationType.OrganisationRejected,
            "Organisation rejected", $"{org.Name} was not approved.",
            org.Id, "Organisation", cancellationToken);

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
        IMirageDbContext db, UserManager<ApplicationUser> userManager, NotificationService notifications,
        CancellationToken cancellationToken)
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

        var user = await userManager.FindByIdAsync(counsellor.UserId.ToString());
        if (user is not null && !await userManager.IsInRoleAsync(user, MirageRoles.Counsellor))
            await userManager.AddToRoleAsync(user, MirageRoles.Counsellor);

        await notifications.NotifyAsync(counsellor.UserId, NotificationType.CounsellorApproved,
            "Counsellor approval", $"You have been approved as a counsellor for {org.Name}.",
            counsellor.Id, "Counsellor", cancellationToken);

        return ApiResults.Ok(context, new { counsellor.Id, counsellor.IsApproved }, "Counsellor approved successfully.");
    }

    // --- Membership ---

    private static async Task<IResult> JoinOrganisation(Guid id, JoinOrganisationRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var org = await db.Organisations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.Status != OrganisationStatus.Approved)
            return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Organisation not active", "Only approved organisations accept members.");

        var existing = await db.OrganisationMembers.SingleOrDefaultAsync(
            x => x.OrganisationId == id && x.UserId == userId, cancellationToken);
        if (existing is not null && existing.Status != OrganisationMemberStatus.Removed
            && existing.Status != OrganisationMemberStatus.Rejected)
            return EndpointHelpers.Conflict(context, "A membership request already exists for this organisation.");

        if (request.BranchId.HasValue &&
            !await db.OrganisationBranches.AnyAsync(x => x.Id == request.BranchId && x.OrganisationId == id, cancellationToken))
            return EndpointHelpers.ValidationProblem(context, ("branchId", "Branch does not belong to this organisation."));

        var member = new OrganisationMember(id, userId, request.BranchId);
        db.OrganisationMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/organisations/{id}/members/{member.Id}",
            new { member.Id, member.Status }, "Membership request submitted successfully.");
    }

    private static async Task<IResult> ListMyMemberships(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var memberships = await db.OrganisationMembers.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.OrganisationId,
                OrganisationName = x.Organisation!.Name,
                x.BranchId,
                x.Status,
                x.AssignedMentorUserId,
                x.AssignedCounsellorUserId,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, memberships, "Your memberships were retrieved successfully.");
    }

    private static async Task<IResult> ListMembers(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;

        var members = await db.OrganisationMembers.AsNoTracking()
            .Where(x => x.OrganisationId == id)
            .Join(db.Profiles.AsNoTracking(), m => m.UserId, p => p.UserId, (m, p) => new OrganisationMemberResponse(
                m.Id, m.UserId, p.DisplayName, p.AvatarUrl, m.BranchId, m.Status,
                m.AssignedMentorUserId, m.AssignedCounsellorUserId, m.CreatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, members, "Members retrieved successfully.");
    }

    private static async Task<IResult> ApproveMember(Guid id, Guid memberId, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;

        var member = await db.OrganisationMembers.SingleOrDefaultAsync(
            x => x.Id == memberId && x.OrganisationId == id, cancellationToken);
        if (member is null) return EndpointHelpers.NotFound(context, "Member was not found.");
        if (member.Status == OrganisationMemberStatus.Approved)
            return EndpointHelpers.Conflict(context, "Member is already approved.");
        member.Approve();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(member.UserId, NotificationType.MembershipApproved,
            "Membership approved", "Your membership request has been approved.",
            member.Id, "OrganisationMember", cancellationToken);

        return ApiResults.Ok(context, new { member.Id, member.Status }, "Member approved successfully.");
    }

    private static async Task<IResult> RejectMember(Guid id, Guid memberId, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;

        var member = await db.OrganisationMembers.SingleOrDefaultAsync(
            x => x.Id == memberId && x.OrganisationId == id, cancellationToken);
        if (member is null) return EndpointHelpers.NotFound(context, "Member was not found.");
        member.Reject();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(member.UserId, NotificationType.MembershipRejected,
            "Membership rejected", "Your membership request was not approved.",
            member.Id, "OrganisationMember", cancellationToken);

        return ApiResults.Ok(context, new { member.Id, member.Status }, "Member rejected successfully.");
    }

    private static async Task<IResult> RemoveMember(Guid id, Guid memberId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;

        var member = await db.OrganisationMembers.SingleOrDefaultAsync(
            x => x.Id == memberId && x.OrganisationId == id, cancellationToken);
        if (member is null) return EndpointHelpers.NotFound(context, "Member was not found.");
        member.Remove();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { member.Id, member.Status }, "Member removed successfully.");
    }

    private static async Task<IResult> AssignMember(Guid id, Guid memberId, AssignMemberRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;

        var member = await db.OrganisationMembers.SingleOrDefaultAsync(
            x => x.Id == memberId && x.OrganisationId == id, cancellationToken);
        if (member is null) return EndpointHelpers.NotFound(context, "Member was not found.");
        if (member.Status != OrganisationMemberStatus.Approved)
            return EndpointHelpers.Conflict(context, "Only approved members can be assigned a mentor or counsellor.");

        member.Assign(request.MentorUserId, request.CounsellorUserId);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context,
            new { member.Id, member.AssignedMentorUserId, member.AssignedCounsellorUserId },
            "Member assignment updated successfully.");
    }

    // --- Branches ---

    private static async Task<IResult> ListBranches(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var branches = await db.OrganisationBranches.AsNoTracking()
            .Where(x => x.OrganisationId == id)
            .OrderBy(x => x.Name)
            .Select(x => new OrganisationBranchResponse(x.Id, x.Name, x.City, x.Country, x.Address))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, branches, "Branches retrieved successfully.");
    }

    private static async Task<IResult> CreateBranch(Guid id, CreateBranchRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.City))
            return EndpointHelpers.ValidationProblem(context, ("branch", "Name and city are required."));

        var branch = new OrganisationBranch(id, request.Name, request.City, request.Country, request.Address);
        db.OrganisationBranches.Add(branch);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/organisations/{id}/branches/{branch.Id}",
            new { branch.Id }, "Branch created successfully.");
    }

    // --- Events + tickets ---

    private static async Task<IResult> ListEvents(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var events = await db.OrgEvents.AsNoTracking()
            .Where(x => x.OrganisationId == id)
            .OrderBy(x => x.StartsAt)
            .Select(x => new OrgEventResponse(x.Id, x.OrganisationId, x.BranchId, x.Title, x.Description, x.ImageUrl,
                x.StartsAt, x.EndsAt, x.Location, x.Capacity, db.EventTickets.Count(t => t.EventId == x.Id)))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, events, "Events retrieved successfully.");
    }

    private static async Task<IResult> CreateEvent(Guid id, CreateEventRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Location))
            return EndpointHelpers.ValidationProblem(context, ("event", "Title and location are required."));
        if (request.EndsAt <= request.StartsAt)
            return EndpointHelpers.ValidationProblem(context, ("endsAt", "End time must be after the start time."));

        var evt = new OrgEvent(id, request.BranchId, context.User.GetUserId(), request.Title, request.Description,
            request.ImageUrl,
            request.StartsAt, request.EndsAt, request.Location, request.Capacity);
        db.OrgEvents.Add(evt);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/organisations/{id}/events/{evt.Id}",
            new { evt.Id }, "Event created successfully.");
    }

    private static async Task<IResult> RegisterForEvent(Guid id, Guid eventId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var evt = await db.OrgEvents.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == eventId && x.OrganisationId == id, cancellationToken);
        if (evt is null) return EndpointHelpers.NotFound(context, "Event was not found.");

        if (await db.EventTickets.AnyAsync(x => x.EventId == eventId && x.UserId == userId, cancellationToken))
            return EndpointHelpers.Conflict(context, "You are already registered for this event.");

        if (evt.Capacity.HasValue)
        {
            var issued = await db.EventTickets.CountAsync(x => x.EventId == eventId, cancellationToken);
            if (issued >= evt.Capacity.Value)
                return EndpointHelpers.Conflict(context, "This event is fully booked.");
        }

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9))
            .Replace('+', 'A').Replace('/', 'B').Replace('=', 'C');
        var ticket = new EventTicket(eventId, userId, code);
        db.EventTickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/organisations/{id}/events/{eventId}/tickets/{ticket.Id}",
            new { ticket.Id, ticket.Code }, "Ticket issued successfully.");
    }

    private static async Task<IResult> ListEventTickets(Guid id, Guid eventId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var forbidden = await RequireOrgAdmin(id, context, db, cancellationToken);
        if (forbidden is not null) return forbidden;

        var tickets = await db.EventTickets.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Join(db.Profiles.AsNoTracking(), t => t.UserId, p => p.UserId, (t, p) => new
            {
                t.Id,
                t.UserId,
                p.DisplayName,
                t.Code,
                t.CheckedInAt
            })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, tickets, "Tickets retrieved successfully.");
    }

    private static async Task<IResult> ListMyTickets(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var tickets = await db.EventTickets.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Join(db.OrgEvents.AsNoTracking(), t => t.EventId, e => e.Id, (t, e) => new { Ticket = t, Event = e })
            .OrderBy(x => x.Event.StartsAt)
            .Select(x => new EventTicketResponse(
                x.Ticket.Id, x.Event.Id, x.Event.Title, x.Event.ImageUrl, x.Event.StartsAt, x.Ticket.Code, x.Ticket.CheckedInAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, tickets, "Your tickets were retrieved successfully.");
    }

    private static async Task<IResult?> RequireOrgAdmin(Guid organisationId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var org = await db.Organisations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == organisationId, cancellationToken);
        if (org is null) return EndpointHelpers.NotFound(context, "Organisation was not found.");
        if (org.AdminUserId != userId && !context.User.IsInRole(MirageRoles.PlatformAdmin))
            return EndpointHelpers.Forbidden(context);
        return null;
    }
}
