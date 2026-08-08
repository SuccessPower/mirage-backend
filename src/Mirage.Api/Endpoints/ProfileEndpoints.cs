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

        // AvatarUrl can only ever be set to a URL that already passed face-detection
        // (UpdateMine), so this alone keeps caricatures/blank photos out of Discovery.
        query = query.Where(x => x.AvatarUrl != null && x.AvatarUrl != ""
            && x.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos);

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

            // A pass only removes the card from the client's current deck. Keep the vote for
            // analytics/history, but do not exclude it from a subsequent discovery request so
            // refreshing starts a fresh deck that can include previously passed profiles.

            var mine = await db.Profiles.AsNoTracking().Where(x => x.UserId == me)
                .Select(x => new { x.City, x.Country, x.CountryCode, x.ContinentCode, x.DiscoveryScope,
                    x.PreferredCountryCodes, x.Sex, x.RelationshipStatus }).SingleOrDefaultAsync(cancellationToken);
            myCity = mine?.City;
            myCountry = mine?.Country;
            mySex = mine?.Sex;
            viewerIsMarried = mine?.RelationshipStatus == RelationshipStatus.Married;

            var viewerHasRequiredPhotos = await db.Profiles.AsNoTracking()
                .AnyAsync(x => x.UserId == me && x.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos,
                    cancellationToken);
            if (!viewerHasRequiredPhotos)
            {
                var viewedIds = db.DiscoveryProfileViews.AsNoTracking()
                    .Where(x => x.ViewerUserId == me).Select(x => x.ProfileUserId);
                var viewedCount = await viewedIds.CountAsync(cancellationToken);
                if (viewedCount >= 2)
                    query = query.Where(x => viewedIds.Contains(x.UserId));
                else
                {
                    page = 1;
                    pageSize = Math.Min(pageSize, 2 - viewedCount);
                }
            }
            if (mine?.DiscoveryScope == DiscoveryScope.Country && mine.CountryCode is not null)
                query = query.Where(x => x.CountryCode == mine.CountryCode);
            else if (mine?.DiscoveryScope == DiscoveryScope.Continent && mine.ContinentCode is not null)
                query = query.Where(x => x.ContinentCode == mine.ContinentCode);
        }

        if (section == SectionCategory.Friendship)
        {
            // Friendship pairs marital peer groups: married members see other married members,
            // everyone else (including guests) sees unmarried members.
            query = viewerIsMarried
                ? query.Where(x => x.RelationshipStatus == RelationshipStatus.Married)
                : query.Where(x => x.RelationshipStatus != RelationshipStatus.Married);
        }
        else if (section == SectionCategory.Marriage)
        {
            // The Marriage tab is a browse-only community of already-married members — not a
            // romantic matching feed — so both genders show up regardless of the viewer's own
            // sex or marital status.
            query = query.Where(x => x.RelationshipStatus == RelationshipStatus.Married);
        }
        else
        {
            // Dating and the default "All" feed never surface married profiles — married members
            // browse couples through /couples/discover instead — and approved couples are off
            // the market.
            query = query.Where(x => x.RelationshipStatus != RelationshipStatus.Married);
            query = query.Where(x => !db.Couples.Any(c => c.Status == CoupleStatus.Approved
                && (c.User1Id == x.UserId || c.User2Id == x.UserId)));
        }

        // Dating and the default "All" feed are opposite-sex only; friendship and marriage have
        // no gender restriction. Skipped entirely if either party's sex isn't on file, rather
        // than hiding everyone.
        if ((section is null or SectionCategory.Dating) && mySex.HasValue)
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
            .ThenByDescending(x => currentUserId.HasValue && db.Profiles.Any(me => me.UserId == currentUserId.Value
                && me.PreferredCountryCodes.Contains(x.CountryCode!)))
            .ThenByDescending(x => currentUserId.HasValue && db.Profiles.Any(me => me.UserId == currentUserId.Value
                && me.ContinentCode != null && x.ContinentCode == me.ContinentCode))
            .ThenByDescending(x => x.IsVerified)
            .ThenByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        if (currentUserId.HasValue)
        {
            var viewerHasRequiredPhotos = await db.Profiles.AsNoTracking().AnyAsync(
                x => x.UserId == currentUserId.Value && x.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos,
                cancellationToken);
            if (!viewerHasRequiredPhotos)
            {
                var alreadyViewed = await db.DiscoveryProfileViews.Where(x => x.ViewerUserId == currentUserId.Value)
                    .Select(x => x.ProfileUserId).ToListAsync(cancellationToken);
                foreach (var profile in pagedProfiles.Items.Where(x => !alreadyViewed.Contains(x.UserId)))
                    db.DiscoveryProfileViews.Add(new DiscoveryProfileView(currentUserId.Value, profile.UserId));
                await db.SaveChangesAsync(cancellationToken);
            }
        }
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
        NotificationService notifications, IEmailService email, IConfiguration configuration, ILoggerFactory loggerFactory,
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

        var visitorUserId = context.User.GetUserId();
        if (visitorUserId != userId)
        {
            if (profile.PhotoUrls.Length < UserProfile.MinimumRequiredPhotos)
                return EndpointHelpers.NotFound(context, "Profile was not found.");

            var visitorHasRequiredPhotos = await db.Profiles.AsNoTracking().AnyAsync(
                x => x.UserId == visitorUserId && x.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos,
                cancellationToken);
            if (!visitorHasRequiredPhotos)
            {
                var existingView = await db.DiscoveryProfileViews.AnyAsync(
                    x => x.ViewerUserId == visitorUserId && x.ProfileUserId == userId, cancellationToken);
                if (!existingView)
                {
                    var views = await db.DiscoveryProfileViews.CountAsync(
                        x => x.ViewerUserId == visitorUserId, cancellationToken);
                    if (views >= 2)
                        return EndpointHelpers.Forbidden(context,
                            "Upload at least two matching photos of yourself to view more profiles.");
                    db.DiscoveryProfileViews.Add(new DiscoveryProfileView(visitorUserId, userId));
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            try
            {
                await RecordProfileVisitAsync(userId, visitorUserId, profile, account.Email, db, notifications,
                    email, configuration, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Visit analytics/notifications are secondary. A transient email/database failure
                // must never make an otherwise valid member profile unavailable.
                loggerFactory.CreateLogger("Mirage.ProfileVisits").LogError(exception,
                    "Could not record profile visit. ProfileUserId: {ProfileUserId}; VisitorUserId: {VisitorUserId}; CorrelationId: {CorrelationId}",
                    userId, visitorUserId, context.TraceIdentifier);
            }
        }

        return ApiResults.Ok(context, profile.ToResponse(recommended, account.Email, badge), "Profile retrieved successfully.");
    }

    private static async Task RecordProfileVisitAsync(Guid profileUserId, Guid visitorUserId,
        UserProfile visitedProfile, string? ownerEmail, MirageDbContext db, NotificationService notifications,
        IEmailService email, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var visitor = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == visitorUserId)
            .Select(x => new { x.DisplayName, x.AvatarUrl, x.Sex, x.RelationshipStatus })
            .SingleOrDefaultAsync(cancellationToken);

        // Married members are outside the romantic profile-visit alert flow. Suppress the visit
        // completely when either participant is married so it consumes neither a reveal slot nor
        // generates an in-app/email alert. Missing sex data is not guessed.
        if (visitor is null || !ProfileVisit.ShouldNotify(
                visitor.Sex, visitor.RelationshipStatus,
                visitedProfile.Sex, visitedProfile.RelationshipStatus))
            return;

        ProfileVisit? visit = null;
        var isNewVisit = false;
        var executionStrategy = db.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            // A retry reuses this scoped DbContext. Remove visit state left by the failed attempt
            // so the retried transaction reads and writes a clean database-backed view.
            foreach (var entry in db.ChangeTracker.Entries<ProfileVisit>().ToArray())
                entry.State = EntityState.Detached;
            visit = null;
            isNewVisit = false;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // The reveal ordinal is a per-profile quota. A transaction-scoped advisory lock makes
            // allocating it deterministic even when several visitors open the profile concurrently.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({profileUserId.ToString()}, 0))",
                cancellationToken);

            var existing = await db.ProfileVisits.SingleOrDefaultAsync(
                x => x.ProfileUserId == profileUserId && x.VisitorUserId == visitorUserId, cancellationToken);
            if (existing is not null)
            {
                existing.RecordReturnVisit();
                visit = existing;
            }
            else
            {
                var revealOrdinal = await db.ProfileVisits.CountAsync(
                    x => x.ProfileUserId == profileUserId, cancellationToken) + 1;
                visit = new ProfileVisit(profileUserId, visitorUserId, revealOrdinal);
                db.ProfileVisits.Add(visit);
                isNewVisit = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        // Returning visitors refresh LastVisitedAt but do not generate repeated notifications.
        if (!isNewVisit || visit is null) return;

        var revealIdentity = visit.IsIdentityRevealed;
        var title = revealIdentity ? $"{visitor.DisplayName} viewed your profile" : "Someone viewed your profile";
        var body = revealIdentity
            ? $"{visitor.DisplayName} visited your profile. This is free visitor reveal {visit.RevealOrdinal} of 10."
            : "Someone visited your profile. Their identity is hidden because your 10 free visitor reveals have been used.";

        await notifications.NotifyAsync(profileUserId, NotificationType.ProfileVisited, title, body,
            revealIdentity ? visitorUserId : null, revealIdentity ? "Profile" : "ProfileVisit",
            cancellationToken, revealIdentity ? $"/profiles/{visitorUserId}" : "/", revealIdentity ? "View profile" : "Open Mirage");

        if (ownerEmail is null) return;
        var frontendUrl = (configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app").TrimEnd('/');
        await email.SendProfileVisitEmailAsync(ownerEmail, visitedProfile.DisplayName, visitor.DisplayName,
            visitor.AvatarUrl, revealIdentity,
            revealIdentity ? $"{frontendUrl}/profiles/{visitorUserId}" : frontendUrl, cancellationToken);
    }

    private static async Task<IResult> GetMine(HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, NotificationService notifications,
        IConfiguration configuration, CancellationToken cancellationToken)
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
        var hasRequiredPhotos = profile.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos;
        if (!hasRequiredPhotos)
        {
            var reminderCutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var recentlyReminded = await db.Notifications.AsNoTracking().AnyAsync(x => x.UserId == userId
                && x.Type == NotificationType.ProfilePhotosRequired && x.CreatedAt >= reminderCutoff,
                cancellationToken);
            if (!recentlyReminded)
                await notifications.NotifyAsync(userId, NotificationType.ProfilePhotosRequired,
                    ProfilePhotoMessages.ReminderTitle, ProfilePhotoMessages.ReminderBody,
                    cancellationToken: cancellationToken,
                    actionUrl: $"{FrontendBaseUrl(configuration)}/profile/edit",
                    actionLabel: "Upload photos");
        }
        var viewedCount = hasRequiredPhotos ? 0 : await db.DiscoveryProfileViews.AsNoTracking()
            .CountAsync(x => x.ViewerUserId == userId, cancellationToken);
        var response = profile.ToResponse(recommended, email, badge) with
        {
            Roles = roles,
            MentorProfileId = mentor?.Id,
            HasApprovedMentorProfile = mentor?.IsApproved == true,
            IsChurchAdmin = roles.Contains(MirageRoles.ChurchAdmin) || roles.Contains(MirageRoles.PlatformAdmin),
            IsCounsellor = roles.Contains(MirageRoles.Counsellor),
            EmailConfirmed = user?.EmailConfirmed,
            HasRequiredProfilePhotos = hasRequiredPhotos,
            DiscoveryProfilesRemaining = hasRequiredPhotos ? int.MaxValue : Math.Max(0, 2 - viewedCount)
        };
        return ApiResults.Ok(context, response, "Profile retrieved successfully.");
    }

    // "Frontend:BaseUrl" is the key that's actually set in appsettings and that the email layer
    // itself reads; the flat "FrontendUrl" some call sites used isn't configured anywhere, so those
    // links always silently fell through to the hardcoded default.
    private static string FrontendBaseUrl(IConfiguration configuration) =>
        configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com";

    // Turns a comparison outcome into the response the member actually reads. Everything that isn't
    // SamePerson used to collapse into "these are different people", which is both wrong and
    // unactionable when the real problem was a photo with no detectable face, a group shot, or a
    // stored photo that no longer analyses cleanly. The two nouns name each side of the comparison
    // so the message points at the photo that needs changing.
    private static IResult? FaceComparisonProblem(HttpContext context, FaceComparisonResult comparison,
        string field, string firstPhoto, string secondPhoto)
        => comparison switch
        {
            FaceComparisonResult.SamePerson => null,
            FaceComparisonResult.Unavailable => EndpointHelpers.Conflict(context,
                "Photo identity verification is temporarily unavailable. Please try again in a moment."),
            FaceComparisonResult.DifferentPerson => EndpointHelpers.ValidationProblem(context, (field,
                "The people in your photos don't look like the same person. Every photo needs to clearly show you.")),
            FaceComparisonResult.NoFaceInFirstPhoto => EndpointHelpers.ValidationProblem(context, (field,
                $"We couldn't find a clear face in {firstPhoto}, so we can't check it against this one. Please replace it with a well-lit photo taken face-on.")),
            FaceComparisonResult.MultipleFacesInFirstPhoto => EndpointHelpers.ValidationProblem(context, (field,
                $"We found more than one face in {firstPhoto}, so there's no single person to match against. Please replace it with a photo of just you.")),
            FaceComparisonResult.NoFaceInSecondPhoto => EndpointHelpers.ValidationProblem(context, (field,
                $"We couldn't find a clear face in {secondPhoto}. Please use a well-lit photo taken face-on, without sunglasses or a heavy filter.")),
            FaceComparisonResult.MultipleFacesInSecondPhoto => EndpointHelpers.ValidationProblem(context, (field,
                $"We found more than one face in {secondPhoto}. Please upload a photo of just you.")),
            _ => EndpointHelpers.Conflict(context,
                "Photo identity verification is temporarily unavailable. Please try again in a moment.")
        };

    private static async Task<IResult> UpdateMine(UpdateProfileRequest request, HttpContext context,
        IMirageDbContext db, ProfileImageValidationService imageValidation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.City))
            return EndpointHelpers.ValidationProblem(context, ("profile", "Display name and city are required."));
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == context.User.GetUserId(), cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");

        // Only re-check when the avatar is actually changing — an unrelated profile edit
        // shouldn't re-validate a photo that already passed the check.
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl) && request.AvatarUrl != profile.AvatarUrl
            && !await imageValidation.IsValidHumanPhotoAsync(request.AvatarUrl, cancellationToken))
        {
            return EndpointHelpers.ValidationProblem(context,
                ("avatarUrl", "We couldn't detect a real, human face in this photo. Please upload a clear photo of your face."));
        }
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl) && request.AvatarUrl != profile.AvatarUrl
            && profile.PhotoUrls.Length > 0)
        {
            var comparison = await imageValidation.AreSamePersonAsync(profile.PhotoUrls[0], request.AvatarUrl,
                cancellationToken);
            var problem = FaceComparisonProblem(context, comparison, "avatarUrl",
                "your existing profile photo", "this photo");
            if (problem is not null) return problem;
        }

        profile.Update(request.DisplayName, request.City, request.Country, request.Denomination,
            request.Bio, request.AnonymityEnabled, request.Interests, request.AvatarUrl, request.Sex,
            request.RelationshipStatus, request.HeightInches, request.SkinTone, request.PreferredLanguage,
            request.Occupation);
        profile.SetInternationalPreferences(request.CountryCode, request.TimeZoneId,
            request.DiscoveryScope, request.PreferredCountryCodes);
        var previousAnniversary = profile.WeddingAnniversaryDate;
        var anniversaryChanged = previousAnniversary != request.WeddingAnniversaryDate;
        profile.SetCelebrationPreferences(request.WeddingAnniversaryDate, request.CelebrationOptOut);

        // A wedding anniversary belongs to the couple, not one spouse — mirror the date onto the
        // approved partner's profile so either can set it once for both. The partner keeps their
        // own CelebrationOptOut.
        if (anniversaryChanged)
        {
            var partnerUserId = await db.Couples.AsNoTracking()
                .Where(c => c.Status == CoupleStatus.Approved
                    && (c.User1Id == profile.UserId || c.User2Id == profile.UserId))
                .Select(c => c.User1Id == profile.UserId ? c.User2Id : c.User1Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (partnerUserId != Guid.Empty)
            {
                var partnerProfile = await db.Profiles
                    .SingleOrDefaultAsync(x => x.UserId == partnerUserId, cancellationToken);
                partnerProfile?.SetCelebrationPreferences(request.WeddingAnniversaryDate,
                    partnerProfile.CelebrationOptOut);
            }

            // A date change makes any current-year anniversary post wrong (it was published for
            // the old date), so remove it — the home banner stops showing it immediately, and the
            // sweep can publish a fresh entry when the corrected date comes around. Only the year
            // component changing keeps the post (same month/day means it's still accurate, and
            // deleting would discard its wishes). Wishes cascade-delete with the entry.
            var sameMonthDay = previousAnniversary is not null && request.WeddingAnniversaryDate is not null
                && previousAnniversary.Value.Month == request.WeddingAnniversaryDate.Value.Month
                && previousAnniversary.Value.Day == request.WeddingAnniversaryDate.Value.Day;
            if (!sameMonthDay)
            {
                // CreatedAt clause catches a banner-visible entry stamped with last year's Year
                // right around New Year; the Year clause catches this year's entry regardless of age.
                var utcNow = DateTimeOffset.UtcNow;
                var currentYear = utcNow.Year;
                var recentCutoff = utcNow.AddDays(-2);
                var featuredIds = partnerUserId == Guid.Empty
                    ? new[] { profile.UserId }
                    : new[] { profile.UserId, partnerUserId };
                await db.CelebrationEntries
                    .Where(e => e.Type == CelebrationType.Anniversary
                        && (featuredIds.Contains(e.UserId)
                            || (e.PartnerUserId != null && featuredIds.Contains(e.PartnerUserId.Value)))
                        && (e.Year == currentYear || e.CreatedAt >= recentCutoff))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.UserId }, "Profile updated successfully.");
    }

    private static async Task<IResult> UpdateMyPhotos(SetProfilePhotosRequest request, HttpContext context,
        IMirageDbContext db, ProfileImageValidationService imageValidation, NotificationService notifications,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        var hadRequiredPhotos = profile.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos;

        var newUrls = request.PhotoUrls.Except(profile.PhotoUrls).ToArray();
        foreach (var url in newUrls)
        {
            if (!await imageValidation.IsValidHumanPhotoAsync(url, cancellationToken))
                return EndpointHelpers.ValidationProblem(context,
                    ("photoUrls", "One of your photos doesn't show a real, human face. Please upload clear photos of yourself."));
        }

        var cleanedUrls = request.PhotoUrls.Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (cleanedUrls.Length >= UserProfile.MinimumRequiredPhotos)
        {
            var photosToCompare = cleanedUrls.Skip(1).ToList();
            if (!string.IsNullOrWhiteSpace(profile.AvatarUrl)) photosToCompare.Add(profile.AvatarUrl);
            foreach (var photo in photosToCompare)
            {
                var comparison = await imageValidation.AreSamePersonAsync(cleanedUrls[0], photo, cancellationToken);
                var problem = FaceComparisonProblem(context, comparison, "photoUrls",
                    "your first photo", "one of your photos");
                if (problem is not null) return problem;
            }
        }

        try { profile.SetPhotos(request.PhotoUrls); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);

        // Crossing the photo threshold is the moment the account stops being restricted, so it's
        // worth telling people out-of-band rather than leaving them to notice. Only on the crossing,
        // and only once ever: someone who swaps a photo, or drops to one and back, has already had
        // this news and doesn't need it again.
        if (!hadRequiredPhotos && profile.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos)
        {
            var alreadyCongratulated = await db.Notifications.AsNoTracking().AnyAsync(
                x => x.UserId == userId && x.Type == NotificationType.ProfilePhotosComplete, cancellationToken);
            if (!alreadyCongratulated)
                await notifications.NotifyAsync(userId, NotificationType.ProfilePhotosComplete,
                    ProfilePhotoMessages.CompleteTitle, ProfilePhotoMessages.CompleteBody,
                    cancellationToken: cancellationToken,
                    actionUrl: $"{FrontendBaseUrl(configuration)}/hub",
                    actionLabel: "Start exploring");
        }

        return ApiResults.Ok(context, new { profile.UserId, profile.PhotoUrls }, "Profile photos updated successfully.");
    }

    // One-time completion of a minimal Google sign-in profile — fills in DOB/city/etc. that
    // registration would normally collect up front, and optionally joins a church in the same step
    // (same self-service church search/propose flow as RegisterRequest).
    private static async Task<IResult> CompleteProfile(CompleteProfileRequest request, HttpContext context,
        IMirageDbContext db, ProfileImageValidationService imageValidation, CancellationToken cancellationToken)
    {
        var errors = ValidateCompleteProfile(request);
        if (errors.Length > 0) return EndpointHelpers.ValidationProblem(context, errors);

        var userId = context.User.GetUserId();
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        if (profile.IsProfileComplete)
            return EndpointHelpers.Conflict(context, "Your profile is already complete.");

        if (!await imageValidation.IsValidHumanPhotoAsync(request.AvatarUrl, cancellationToken))
            return EndpointHelpers.ValidationProblem(context,
                ("avatarUrl", "We couldn't detect a real, human face in this photo. Please upload a clear photo of your face."));

        var churchSelection = await ChurchSelectionResolver.ResolveAsync(userId, request.Denomination, request.Country,
            request.OrganisationId, request.BranchId, request.NewOrganisationName,
            request.NewOrganisationRegistrationNumber, request.NewBranchName, request.NewBranchCity,
            context, db, cancellationToken);
        if (churchSelection.Error is not null) return churchSelection.Error;

        profile.CompleteProfile(request.DateOfBirth, request.City, request.Country, request.Denomination,
            request.Bio, request.AvatarUrl, request.Sex, request.RelationshipStatus, request.Occupation);
        profile.SetInternationalPreferences(request.CountryCode, request.TimeZoneId,
            request.DiscoveryScope, request.PreferredCountryCodes);
        profile.Verify();

        if (churchSelection.OrganisationId.HasValue)
            await OrganisationMembershipService.AddMemberAsync(db, churchSelection.OrganisationId.Value,
                userId, churchSelection.BranchId, profile, cancellationToken);

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
        if (!EndpointHelpers.IsAtLeast18(request.DateOfBirth))
            errors.Add(("dateOfBirth", "Users must be at least 18 years old."));
        else if (!EndpointHelpers.IsPlausibleBirthDate(request.DateOfBirth))
            errors.Add(("dateOfBirth", "Please enter a valid date of birth."));
        if (string.IsNullOrWhiteSpace(request.City)) errors.Add(("city", "City is required."));
        if (string.IsNullOrWhiteSpace(request.Country)) errors.Add(("country", "Country is required."));
        if (string.IsNullOrWhiteSpace(request.Denomination))
            errors.Add(("denomination", "Select your denomination."));
        if (string.IsNullOrWhiteSpace(request.Bio) || request.Bio.Trim().Length < 20)
            errors.Add(("bio", "Write at least 20 characters about yourself."));
        if (string.IsNullOrWhiteSpace(request.AvatarUrl)) errors.Add(("avatarUrl", "A clear profile photo is required."));
        if (request.Sex is null) errors.Add(("sex", "Select your sex."));
        if (request.RelationshipStatus is null) errors.Add(("relationshipStatus", "Select your relationship status."));
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
        // Tracked (not AsNoTracking) so an instant-join church can verify the profile below.
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
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

        await OrganisationMembershipService.AddMemberAsync(db, churchSelection.OrganisationId.Value,
            userId, churchSelection.BranchId, profile, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await ChurchCommunityService.JoinChurchCommunityAsync(db, churchSelection.OrganisationId.Value,
            Community.ChurchGeneralCategory, userId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context, new { OrganisationId = churchSelection.OrganisationId },
            "Church joined successfully.");
    }
}
