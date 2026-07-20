using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class ProfileEndpoints
{
    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/profiles").WithTags("Profiles");
        group.MapGet("/", Discover);
        group.MapGet("/{userId:guid}", GetById).RequireAuthorization();
        group.MapGet("/me", GetMine).RequireAuthorization();
        group.MapPut("/me", UpdateMine).RequireAuthorization();
        group.MapPut("/me/photos", UpdateMyPhotos).RequireAuthorization();
        group.MapPost("/me/complete", CompleteProfile).RequireAuthorization();
        group.MapPost("/me/church", JoinChurch).RequireAuthorization();
        group.MapGet("/votes/mine", GetMyVotes).RequireAuthorization();
        group.MapPost("/{userId:guid}/vote", CastVote).RequireAuthorization();
        group.MapDelete("/{userId:guid}/vote", RemoveVote).RequireAuthorization();
        return api;
    }

    // Personal feed control, not a public score: a downvote hides the target from the voter's
    // feed, an upvote boosts the target in the voter's ranking. Only the voter ever sees it.
    private static async Task<IResult> CastVote(Guid userId, CastVoteRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.Value != 1 && request.Value != -1)
            return EndpointHelpers.ValidationProblem(context, ("value", "Vote value must be 1 (up) or -1 (down)."));

        var voterId = context.User.GetUserId();
        if (voterId == userId)
            return EndpointHelpers.ValidationProblem(context, ("userId", "You cannot vote on your own profile."));
        if (!await db.Profiles.AsNoTracking().AnyAsync(x => x.UserId == userId, cancellationToken))
            return EndpointHelpers.NotFound(context, "Profile was not found.");

        var vote = await db.ProfileVotes.SingleOrDefaultAsync(
            x => x.VoterUserId == voterId && x.TargetUserId == userId, cancellationToken);
        if (vote is null) db.ProfileVotes.Add(new ProfileVote(voterId, userId, request.Value));
        else vote.ChangeValue(request.Value);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context, new { targetUserId = userId, myVote = request.Value },
            "Vote recorded successfully.");
    }

    private static async Task<IResult> RemoveVote(Guid userId, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var voterId = context.User.GetUserId();
        var vote = await db.ProfileVotes.SingleOrDefaultAsync(
            x => x.VoterUserId == voterId && x.TargetUserId == userId, cancellationToken);
        if (vote is null) return EndpointHelpers.NotFound(context, "Vote was not found.");

        db.ProfileVotes.Remove(vote);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { targetUserId = userId }, "Vote removed successfully.");
    }

    private static async Task<IResult> GetMyVotes(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var voterId = context.User.GetUserId();
        var votes = await db.ProfileVotes.AsNoTracking()
            .Where(x => x.VoterUserId == voterId)
            .Select(x => new { x.TargetUserId, x.Value })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, votes, "Your votes were retrieved successfully.");
    }

    private static async Task<IResult> Discover(HttpContext context, MirageDbContext db,
        SectionCategory? section, string? city,
        string? denomination, int? minAge, int? maxAge, string? search, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (minAge is < 18 || maxAge is > 100 || minAge > maxAge)
            return EndpointHelpers.ValidationProblem(context,
                ("age", "Age filters must be between 18 and 100, with minAge not exceeding maxAge."));

        var query = db.Profiles.AsNoTracking().AsQueryable();

        // Deactivated/deleted accounts (ApplicationUser.IsActive = false) never surface here.
        query = query.Where(x => db.Users.Any(u => u.Id == x.UserId && u.IsActive));

        // A Google sign-in that hasn't finished CompleteProfile yet has a sentinel DOB and blank
        // city/denomination — not fit to show in Discovery until they fill it in.
        query = query.Where(x => x.IsProfileComplete);

        var currentUserId = context.User.TryGetUserId();
        string? myCity = null;
        string? myCountry = null;
        Sex? mySex = null;
        var viewerIsMarried = false;
        if (currentUserId.HasValue)
        {
            var me = currentUserId.Value;
            query = query.Where(x => x.UserId != me);

            // Once you've liked or matched someone, they drop out of discovery — unless that
            // conversation has since been ended (match Closed), in which case they resurface
            // and a fresh like restarts the request/approve handshake.
            var likedIds = db.Likes.Where(x => x.SourceUserId == me).Select(x => x.TargetUserId);
            var openMatchedIds = db.Matches
                .Where(x => (x.User1Id == me || x.User2Id == me) && x.Status != MatchStatus.Closed)
                .Select(x => x.User1Id == me ? x.User2Id : x.User1Id);
            var closedMatchedIds = db.Matches
                .Where(x => (x.User1Id == me || x.User2Id == me) && x.Status == MatchStatus.Closed)
                .Select(x => x.User1Id == me ? x.User2Id : x.User1Id);
            query = query.Where(x => !openMatchedIds.Contains(x.UserId)
                && !(likedIds.Contains(x.UserId) && !closedMatchedIds.Contains(x.UserId)));

            // Downvotes are a personal, permanent hide for the viewer's own feed.
            var downvotedIds = db.ProfileVotes.Where(v => v.VoterUserId == me && v.Value < 0)
                .Select(v => v.TargetUserId);
            query = query.Where(x => !downvotedIds.Contains(x.UserId));

            var mine = await db.Profiles.AsNoTracking().Where(x => x.UserId == me)
                .Select(x => new { x.City, x.Country, x.Sex, x.RelationshipStatus }).SingleOrDefaultAsync(cancellationToken);
            myCity = mine?.City;
            myCountry = mine?.Country;
            mySex = mine?.Sex;
            viewerIsMarried = mine?.RelationshipStatus == RelationshipStatus.Married;
        }

        if (section == SectionCategory.Friendship)
        {
            // Friendship pairs marital peer groups: married members see other married members,
            // everyone else (including guests) sees unmarried members.
            query = viewerIsMarried
                ? query.Where(x => x.RelationshipStatus == RelationshipStatus.Married)
                : query.Where(x => x.RelationshipStatus != RelationshipStatus.Married);
        }
        else
        {
            // Married profiles never appear in romantic feeds — married members browse couples
            // through /couples/discover instead — and approved couples are off the market.
            query = query.Where(x => x.RelationshipStatus != RelationshipStatus.Married);
            query = query.Where(x => !db.Couples.Any(c => c.Status == CoupleStatus.Approved
                && (c.User1Id == x.UserId || c.User2Id == x.UserId)));
        }

        // Dating and marriage are opposite-sex only; friendship has no gender restriction.
        // Skipped entirely if either party's sex isn't on file, rather than hiding everyone.
        if (section is SectionCategory.Dating or SectionCategory.Marriage && mySex.HasValue)
            query = query.Where(x => x.Sex != null && x.Sex != mySex);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(x => EF.Functions.ILike(x.City, $"%{city.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(denomination))
            query = query.Where(x => EF.Functions.ILike(x.Denomination, $"%{denomination.Trim()}%"));

        // Free-text search across the fields a member would naturally type into one box —
        // name, city, denomination, or occupation — as a single case-insensitive contains.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.DisplayName, term)
                || EF.Functions.ILike(x.City, term)
                || EF.Functions.ILike(x.Denomination, term)
                || (x.Occupation != null && EF.Functions.ILike(x.Occupation, term)));
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (minAge.HasValue)
        {
            var latestBirthDate = today.AddYears(-minAge.Value);
            query = query.Where(x => x.DateOfBirth <= latestBirthDate);
        }
        if (maxAge.HasValue)
        {
            var earliestBirthDate = today.AddYears(-(maxAge.Value + 1)).AddDays(1);
            query = query.Where(x => x.DateOfBirth >= earliestBirthDate);
        }
        var recommendedIds = db.Recommendations.AsNoTracking()
            .Where(x => x.Status == RecommendationStatus.Active).Select(x => x.RecommendedUserId);
        // Nearest-first: same city, then same country, before falling back to verified/recency.
        // Profiles the viewer upvoted are boosted to the top of their personal feed.
        var pagedProfiles = await query
            .OrderByDescending(x => currentUserId.HasValue && db.ProfileVotes.Any(
                v => v.VoterUserId == currentUserId.Value && v.TargetUserId == x.UserId && v.Value > 0))
            .ThenByDescending(x => myCity != null && x.City == myCity)
            .ThenByDescending(x => myCountry != null && x.Country == myCountry)
            .ThenByDescending(x => x.IsVerified)
            .ThenByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(page, pageSize, cancellationToken);
        var recommendedUserIds = await recommendedIds
            .Where(userId => pagedProfiles.Items.Select(profile => profile.UserId).Contains(userId))
            .ToListAsync(cancellationToken);
        var pagedUserIds = pagedProfiles.Items.Select(profile => profile.UserId).ToArray();

        // Emails are only ever shown to signed-in viewers — an anonymous visitor browsing
        // Discovery should not be able to harvest every listed member's email address.
        var emails = currentUserId.HasValue
            ? await db.Users.AsNoTracking()
                .Where(user => pagedUserIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken)
            : new Dictionary<Guid, string?>();
        var badges = await db.GetOrgBadgesAsync(pagedUserIds, cancellationToken);
        var response = new Mirage.Application.Common.PagedResult<ProfileResponse>(
            pagedProfiles.Items
                .Select(profile => profile.ToResponse(recommendedUserIds.Contains(profile.UserId),
                    emails.GetValueOrDefault(profile.UserId), badges.GetValueOrDefault(profile.UserId)))
                .ToList(),
            pagedProfiles.Page,
            pagedProfiles.PageSize,
            pagedProfiles.TotalCount);
        return ApiResults.Ok(context, response,
            "Profiles retrieved successfully.");
    }

    private static async Task<IResult> GetById(Guid userId, HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        var account = await db.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => new { user.Email, user.IsActive }).SingleOrDefaultAsync(cancellationToken);
        if (account is null || !account.IsActive) return EndpointHelpers.NotFound(context, "Profile was not found.");
        var recommended = await db.Recommendations.AnyAsync(
            x => x.RecommendedUserId == userId && x.Status == RecommendationStatus.Active, cancellationToken);
        var badge = await db.GetOrgBadgeAsync(userId, cancellationToken);
        return ApiResults.Ok(context, profile.ToResponse(recommended, account.Email, badge), "Profile retrieved successfully.");
    }

    private static async Task<IResult> GetMine(HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        var recommended = await db.Recommendations.AnyAsync(
            x => x.RecommendedUserId == userId && x.Status == RecommendationStatus.Active, cancellationToken);
        var email = await db.Users.AsNoTracking().Where(user => user.Id == userId)
            .Select(user => user.Email).SingleOrDefaultAsync(cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString());
        var roles = user is null ? [] : (await userManager.GetRolesAsync(user)).ToArray();
        var mentor = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.IsApproved })
            .SingleOrDefaultAsync(cancellationToken);
        var badge = await db.GetOrgBadgeAsync(userId, cancellationToken);
        var response = profile.ToResponse(recommended, email, badge) with
        {
            Roles = roles,
            MentorProfileId = mentor?.Id,
            HasApprovedMentorProfile = mentor?.IsApproved == true,
            IsChurchAdmin = roles.Contains(MirageRoles.ChurchAdmin) || roles.Contains(MirageRoles.PlatformAdmin),
            IsCounsellor = roles.Contains(MirageRoles.Counsellor),
            EmailConfirmed = user?.EmailConfirmed
        };
        return ApiResults.Ok(context, response, "Profile retrieved successfully.");
    }

    private static async Task<IResult> UpdateMine(UpdateProfileRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.City))
            return EndpointHelpers.ValidationProblem(context, ("profile", "Display name and city are required."));
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == context.User.GetUserId(), cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        profile.Update(request.DisplayName, request.City, request.Country, request.Denomination,
            request.Bio, request.AnonymityEnabled, request.Interests, request.AvatarUrl, request.Sex,
            request.RelationshipStatus, request.HeightInches, request.SkinTone, request.PreferredLanguage,
            request.Occupation);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.UserId }, "Profile updated successfully.");
    }

    private static async Task<IResult> UpdateMyPhotos(SetProfilePhotosRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == context.User.GetUserId(), cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        try { profile.SetPhotos(request.PhotoUrls); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.UserId, profile.PhotoUrls }, "Profile photos updated successfully.");
    }

    // One-time completion of a minimal Google sign-in profile — fills in DOB/city/etc. that
    // registration would normally collect up front, and optionally joins a church in the same step
    // (same self-service church search/propose flow as RegisterRequest).
    private static async Task<IResult> CompleteProfile(CompleteProfileRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var errors = ValidateCompleteProfile(request);
        if (errors.Length > 0) return EndpointHelpers.ValidationProblem(context, errors);

        var userId = context.User.GetUserId();
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        if (profile.IsProfileComplete)
            return EndpointHelpers.Conflict(context, "Your profile is already complete.");

        var churchSelection = await ChurchSelectionResolver.ResolveAsync(userId, request.Denomination, request.Country,
            request.OrganisationId, request.BranchId, request.NewOrganisationName,
            request.NewOrganisationRegistrationNumber, request.NewBranchName, request.NewBranchCity,
            context, db, cancellationToken);
        if (churchSelection.Error is not null) return churchSelection.Error;

        profile.CompleteProfile(request.DateOfBirth, request.City, request.Country, request.Denomination,
            request.Bio, request.Sex, request.RelationshipStatus, request.Occupation);

        if (churchSelection.OrganisationId.HasValue)
            db.OrganisationMembers.Add(new OrganisationMember(churchSelection.OrganisationId.Value, userId, churchSelection.BranchId));

        await db.SaveChangesAsync(cancellationToken);

        if (churchSelection.OrganisationId.HasValue)
        {
            await ChurchCommunityService.JoinChurchCommunityAsync(db, churchSelection.OrganisationId.Value,
                Community.ChurchGeneralCategory, userId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ApiResults.Ok(context, new { profile.UserId, profile.IsProfileComplete }, "Profile completed successfully.");
    }

    private static (string Field, string Error)[] ValidateCompleteProfile(CompleteProfileRequest request)
    {
        var errors = new List<(string, string)>();
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))
            errors.Add(("dateOfBirth", "Users must be at least 18 years old."));
        if (string.IsNullOrWhiteSpace(request.City)) errors.Add(("city", "City is required."));
        if (string.IsNullOrWhiteSpace(request.Country)) errors.Add(("country", "Country is required."));
        if (!string.IsNullOrWhiteSpace(request.Denomination) &&
            !Enum.TryParse<ChristianDenomination>(request.Denomination, ignoreCase: true, out _))
            errors.Add(("denomination", "Select a valid denomination."));
        return errors.ToArray();
    }

    // The lighter "add your church" nudge for a profile that's already complete but skipped
    // picking a church at signup — same resolver, just without the rest of profile completion.
    private static async Task<IResult> JoinChurch(JoinChurchRequest request, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");

        if (await db.OrganisationMembers.AnyAsync(x => x.UserId == userId &&
                x.Status != OrganisationMemberStatus.Removed && x.Status != OrganisationMemberStatus.Rejected,
                cancellationToken))
            return EndpointHelpers.Conflict(context, "You already belong to another organisation. Leave it before joining a new one.");

        var churchSelection = await ChurchSelectionResolver.ResolveAsync(userId, profile.Denomination, profile.Country,
            request.OrganisationId, request.BranchId, request.NewOrganisationName,
            request.NewOrganisationRegistrationNumber, request.NewBranchName, request.NewBranchCity,
            context, db, cancellationToken);
        if (churchSelection.Error is not null) return churchSelection.Error;
        if (churchSelection.OrganisationId is null)
            return EndpointHelpers.ValidationProblem(context, ("organisationId", "Select or propose a church."));

        db.OrganisationMembers.Add(new OrganisationMember(churchSelection.OrganisationId.Value, userId, churchSelection.BranchId));
        await db.SaveChangesAsync(cancellationToken);

        await ChurchCommunityService.JoinChurchCommunityAsync(db, churchSelection.OrganisationId.Value,
            Community.ChurchGeneralCategory, userId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context, new { OrganisationId = churchSelection.OrganisationId },
            "Church joined successfully.");
    }
}
