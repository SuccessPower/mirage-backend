using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Mirage.Api.Contracts;
using Mirage.Api.Middleware;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;
using Npgsql;

namespace Mirage.Api.Endpoints;

internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Authentication");
        group.MapPost("/register", Register);
        group.MapPost("/register/counsellor", RegisterCounsellor);
        group.MapPost("/register/counsellor/independent", RegisterIndependentCounsellor);
        group.MapPost("/register/mentor", RegisterMentor);
        group.MapPost("/login", Login);
        group.MapPost("/verify-password", VerifyPassword).RequireAuthorization();
        group.MapPost("/google", GoogleAuth);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapPost("/logout-all", LogoutAll).RequireAuthorization();
        group.MapPost("/change-password", ChangePassword).RequireAuthorization();
        group.MapPost("/deactivate-account", DeactivateAccount).RequireAuthorization();
        group.MapPost("/delete-account", DeleteAccount).RequireAuthorization();
        group.MapPost("/forgot-password", ForgotPassword);
        group.MapPost("/reset-password", ResetPassword);
        group.MapPost("/confirm-email", ConfirmEmail);
        group.MapPost("/resend-confirmation", ResendConfirmation);
        group.MapGet("/sessions", GetSessions).RequireAuthorization();
        group.MapGet("/session", GetCurrentSession).RequireAuthorization();
        return api;
    }

    /// <summary>
    /// The caller's live roles and practice memberships, read from the database rather than from
    /// their access token. A mentor or counsellor approved after their token was issued carries a
    /// token with no such role until it next refreshes, which left them staring at a member's view
    /// of the app — no practice page, no incoming requests — until they signed out and back in.
    /// Clients gate practice UI on this, not on the token's role claim.
    /// </summary>
    private static async Task<IResult> GetCurrentSession(HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "Account was not found.");
        var roles = await userManager.GetRolesAsync(user);

        var mentor = await db.Mentors.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.IsApproved, x.OffersPaidMentorship })
            .SingleOrDefaultAsync(cancellationToken);
        var counsellor = await db.Counsellors.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.IsApproved })
            .SingleOrDefaultAsync(cancellationToken);

        return ApiResults.Ok(context, new
        {
            UserId = userId,
            Roles = roles,
            MentorProfileId = mentor?.Id,
            IsMentor = mentor is { IsApproved: true },
            HasMentorApplication = mentor is not null,
            OffersPaidMentorship = mentor?.OffersPaidMentorship ?? false,
            CounsellorProfileId = counsellor?.Id,
            IsCounsellor = counsellor is { IsApproved: true },
            HasCounsellorApplication = counsellor is not null,
        }, "Session retrieved successfully.");
    }

    private static async Task<IResult> Register(RegisterRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IMemoryCache cache,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, ILoggerFactory loggerFactory,
        IEmailService emailService,
        IServiceScopeFactory scopeFactory,
        NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Length > 0) return EndpointHelpers.ValidationProblem(context, errors);
        if (!await ProfessionalInviteEndpoints.IsValidCode(request.ProfessionalInviteCode, db, cancellationToken))
            return EndpointHelpers.ValidationProblem(context,
                ("professionalInviteCode", "Mentor or counsellor invite code is invalid."));

        var logger = loggerFactory.CreateLogger("Mirage.Performance.Registration");
        var registrationStopwatch = Stopwatch.StartNew();
        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        var normalizedUserName = userManager.NormalizeName(request.Email.Trim());
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            UserName = request.Email.Trim(),
            NormalizedUserName = normalizedUserName,
            EmailConfirmed = false,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            IsNewsletterSubscribed = request.SubscribeToNewsletter,
            NewsletterSubscribedAt = request.SubscribeToNewsletter ? DateTimeOffset.UtcNow : null
        };

        var passwordValidationStarted = registrationStopwatch.Elapsed.TotalMilliseconds;
        var passwordErrors = new List<IdentityError>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, request.Password);
            if (!result.Succeeded) passwordErrors.AddRange(result.Errors);
        }
        if (passwordErrors.Count > 0)
            return EndpointHelpers.ValidationProblem(context,
                passwordErrors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()),
                "Registration validation failed.");

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        var passwordHashingMs = registrationStopwatch.Elapsed.TotalMilliseconds - passwordValidationStarted;

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                var databaseStarted = registrationStopwatch.Elapsed.TotalMilliseconds;
                var roleId = await cache.GetOrCreateAsync(IdentityCacheKeys.DefaultUserRoleId, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return await db.Roles.AsNoTracking()
                        .Where(x => x.NormalizedName == MirageRoles.User.ToUpperInvariant())
                        .Select(x => x.Id)
                        .SingleAsync(cancellationToken);
                });

                var refreshValue = tokens.CreateRefreshToken();
                var refreshDays = RefreshTokenDays(context, configuration);
                var refreshToken = new RefreshToken(
                    user.Id,
                    refreshValue,
                    DateTimeOffset.UtcNow.AddDays(refreshDays));
                var accessToken = tokens.CreateAccessToken(user, [MirageRoles.User]);

                var churchSelection = await ChurchSelectionResolver.ResolveAsync(user.Id, request.Denomination,
                    request.Country, request.OrganisationId, request.BranchId, request.NewOrganisationName,
                    request.NewOrganisationRegistrationNumber, request.NewBranchName, request.NewBranchCity,
                    context, db, cancellationToken);
                if (churchSelection.Error is not null) return churchSelection.Error;

                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
                var newProfile = new UserProfile(user.Id, request.DisplayName, request.DateOfBirth, request.City,
                    request.Country, request.Denomination, request.Bio, request.Sex,
                    request.RelationshipStatus, request.Occupation, GetClientIpAddress(context));
                newProfile.SetInternationalPreferences(request.CountryCode, request.TimeZoneId,
                    request.DiscoveryScope, request.PreferredCountryCodes);
                db.Profiles.Add(newProfile);
                db.RefreshTokens.Add(refreshToken);
                if (churchSelection.OrganisationId.HasValue)
                    await OrganisationMembershipService.AddMemberAsync(db, churchSelection.OrganisationId.Value,
                        user.Id, churchSelection.BranchId, newProfile, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                if (!await ProfessionalInviteEndpoints.RedeemCode(user.Id, request.ProfessionalInviteCode, db,
                        notifications, cancellationToken))
                    return EndpointHelpers.ValidationProblem(context,
                        ("professionalInviteCode", "Mentor or counsellor invite code is invalid."));

                // Joining a church's community is immediate — it isn't gated on the ChurchAdmin
                // approving the OrganisationMember row above (which only stays Pending when the
                // org requires approval or is itself still under review).
                if (churchSelection.OrganisationId.HasValue)
                {
                    await ChurchCommunityService.JoinChurchCommunityAsync(db, churchSelection.OrganisationId.Value,
                        Community.ChurchGeneralCategory, user.Id, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                }

                // Someone may have asked to sync with this address before it had an account (see
                // CoupleEndpoints.Invite parking a PartnerInvite). Those parked invites become real
                // Couple invitations now, so the inviter never has to send the request twice.
                var inviteeEmail = PartnerInvite.Normalise(user.Email!);
                var pendingPartnerInvites = await db.PartnerInvites
                    .Where(x => x.InviteeEmail == inviteeEmail && x.Status == PartnerInviteStatus.Pending)
                    .ToListAsync(cancellationToken);
                var acceptedPartnerInvites = new List<(Guid InviterUserId, Guid CoupleId)>();
                foreach (var partnerInvite in pendingPartnerInvites)
                {
                    if (partnerInvite.InviterUserId == user.Id) continue;
                    var couple = new Couple(partnerInvite.InviterUserId, user.Id);
                    db.Couples.Add(couple);
                    partnerInvite.MarkAccepted(couple.Id);
                    acceptedPartnerInvites.Add((partnerInvite.InviterUserId, couple.Id));
                }
                if (acceptedPartnerInvites.Count > 0) await db.SaveChangesAsync(cancellationToken);

                var confirmToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
                var appUrl = configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
                var confirmUrl = $"{appUrl}/confirm-email?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(confirmToken)}";

                // The welcome email doubles as the confirmation email (one send, not two) and is
                // dispatched off the request path so the client gets its response immediately — the
                // outbound provider call otherwise adds seconds of latency here. WelcomeEmailBackfillWorker
                // sweeps for missed WelcomeEmailSentAt as a safety net, and /auth/resend-confirmation
                // covers a dropped confirmation link.
                var userId = user.Id;
                var userEmail = user.Email!;
                var displayName = request.DisplayName;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = scopeFactory.CreateScope();
                        var scopedEmailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                        var scopedDb = scope.ServiceProvider.GetRequiredService<MirageDbContext>();

                        if (acceptedPartnerInvites.Count > 0)
                        {
                            var scopedNotifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
                            foreach (var (inviterUserId, coupleId) in acceptedPartnerInvites)
                            {
                                var inviterName = await scopedDb.Profiles.AsNoTracking()
                                    .Where(x => x.UserId == inviterUserId).Select(x => x.DisplayName)
                                    .SingleOrDefaultAsync(CancellationToken.None) ?? "Your partner";
                                await scopedNotifications.NotifyAsync(inviterUserId, NotificationType.NewMatch,
                                    "Your partner joined Mirage",
                                    $"{displayName} has signed up. Your partner sync request is waiting for them to approve.",
                                    coupleId, "Couple", CancellationToken.None);
                                await scopedNotifications.NotifyAsync(userId, NotificationType.NewMatch,
                                    "Spouse link request",
                                    $"{inviterName} wants to link with you as a married couple.",
                                    coupleId, "Couple", CancellationToken.None);
                            }
                        }

                        var welcomeEmailSent = await scopedEmailService.SendWelcomeEmailAsync(
                            userEmail, displayName, confirmUrl, CancellationToken.None);
                        if (welcomeEmailSent)
                        {
                            var trackedUser = await scopedDb.Users.FindAsync([userId], CancellationToken.None);
                            if (trackedUser is not null)
                            {
                                trackedUser.WelcomeEmailSentAt = DateTimeOffset.UtcNow;
                                await scopedDb.SaveChangesAsync(CancellationToken.None);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Post-registration email dispatch failed for user {UserId}", userId);
                    }
                });

                var databaseMs = registrationStopwatch.Elapsed.TotalMilliseconds - databaseStarted;
                ResponseTimeMiddleware.SetServerTiming(context, "password", passwordHashingMs);
                ResponseTimeMiddleware.SetServerTiming(context, "database", databaseMs);
                logger.LogInformation(
                    "Registration completed in {TotalMilliseconds:F3} ms. PasswordHashing: {PasswordHashingMilliseconds:F3} ms; " +
                    "Database: {DatabaseMilliseconds:F3} ms; UserId: {UserId}",
                    registrationStopwatch.Elapsed.TotalMilliseconds,
                    passwordHashingMs,
                    databaseMs,
                    user.Id);

                return ApiResults.Ok(context,
                    new AuthResponse(accessToken.Token, accessToken.ExpiresAt, refreshValue),
                    "Registration completed successfully.");
            });
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                 { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogWarning("Registration rejected by a uniqueness constraint for normalized email {NormalizedEmail}",
                normalizedEmail);
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]>
                {
                    ["DuplicateUserName"] = ["An account with this email already exists."]
                },
                "Registration validation failed.");
        }
    }

    private static async Task<IResult> RegisterCounsellor(RegisterCounsellorRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        IPasswordHasher<ApplicationUser> passwordHasher,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InviteToken))
            return EndpointHelpers.ValidationProblem(context, ("inviteToken", "Invite token is required."));
        if (string.IsNullOrWhiteSpace(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "Email is required."));
        if (!new EmailAddressAttribute().IsValid(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "A valid email address is required."));
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return EndpointHelpers.ValidationProblem(context, ("displayName", "Display name is required."));
        if (!EndpointHelpers.IsAtLeast18(request.DateOfBirth))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Counsellors must be at least 18 years old."));
        if (!EndpointHelpers.IsPlausibleBirthDate(request.DateOfBirth))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Please enter a valid date of birth."));
        if (request.YearsExperience < 0)
            return EndpointHelpers.ValidationProblem(context, ("yearsExperience", "Years of experience must be 0 or greater."));

        var tokenHash = CounsellorInvite.ComputeHash(request.InviteToken);
        var invite = await db.CounsellorInvites
            .Include(x => x.Organisation)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (invite is null || !invite.IsValid)
            return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Invalid invite", "This invite token is invalid or has already been used.");

        if (!invite.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Invalid invite", "The email address does not match the invite.");

        if (invite.Organisation.Status != OrganisationStatus.Approved)
            return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Invalid invite", "The organisation associated with this invite is not active.");

        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            UserName = request.Email.Trim(),
            NormalizedUserName = userManager.NormalizeName(request.Email.Trim()),
            EmailConfirmed = false,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };

        var passwordErrors = new List<IdentityError>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, request.Password);
            if (!result.Succeeded) passwordErrors.AddRange(result.Errors);
        }
        if (passwordErrors.Count > 0)
            return EndpointHelpers.ValidationProblem(context,
                passwordErrors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()),
                "Registration validation failed.");

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var counsellorRoleId = await db.Roles.AsNoTracking()
            .Where(x => x.NormalizedName == MirageRoles.Counsellor.ToUpperInvariant())
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);

        var refreshValue = tokens.CreateRefreshToken();
        var refreshDays = RefreshTokenDays(context, configuration);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = counsellorRoleId });
                db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth,
                    request.City, request.Country, request.Denomination, request.Bio));
                db.Counsellors.Add(new CounsellorProfile(user.Id, invite.OrganisationId,
                    request.YearsExperience, request.Specialisations, request.Languages));
                db.RefreshTokens.Add(new RefreshToken(user.Id, refreshValue, DateTimeOffset.UtcNow.AddDays(refreshDays)));
                invite.Redeem();
                await db.SaveChangesAsync(cancellationToken);
                var accessToken = tokens.CreateAccessToken(user, [MirageRoles.Counsellor]);
                return ApiResults.Ok(context,
                    new AuthResponse(accessToken.Token, accessToken.ExpiresAt, refreshValue),
                    "Counsellor registration completed successfully.");
            });
        }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException
                                                  { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]>
                {
                    ["DuplicateUserName"] = ["An account with this email already exists."]
                },
                "Registration validation failed.");
        }
    }

    // Open self-signup for counsellors not affiliated with any organisation — no invite token,
    // but requires verification documents and stays unapproved until a PlatformAdmin reviews them.
    private static async Task<IResult> RegisterIndependentCounsellor(RegisterIndependentCounsellorRequest request,
        HttpContext context, UserManager<ApplicationUser> userManager, IPasswordHasher<ApplicationUser> passwordHasher,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "Email is required."));
        if (!new EmailAddressAttribute().IsValid(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "A valid email address is required."));
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return EndpointHelpers.ValidationProblem(context, ("displayName", "Display name is required."));
        if (!EndpointHelpers.IsAtLeast18(request.DateOfBirth))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Counsellors must be at least 18 years old."));
        if (!EndpointHelpers.IsPlausibleBirthDate(request.DateOfBirth))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Please enter a valid date of birth."));
        if (request.YearsExperience < 0)
            return EndpointHelpers.ValidationProblem(context, ("yearsExperience", "Years of experience must be 0 or greater."));

        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            UserName = request.Email.Trim(),
            NormalizedUserName = userManager.NormalizeName(request.Email.Trim()),
            EmailConfirmed = false,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };

        var passwordErrors = new List<IdentityError>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, request.Password);
            if (!result.Succeeded) passwordErrors.AddRange(result.Errors);
        }
        if (passwordErrors.Count > 0)
            return EndpointHelpers.ValidationProblem(context,
                passwordErrors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()),
                "Registration validation failed.");

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var counsellorRoleId = await db.Roles.AsNoTracking()
            .Where(x => x.NormalizedName == MirageRoles.Counsellor.ToUpperInvariant())
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);

        var refreshValue = tokens.CreateRefreshToken();
        var refreshDays = RefreshTokenDays(context, configuration);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = counsellorRoleId });
                db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth,
                    request.City, request.Country, request.Denomination, request.Bio));
                db.Counsellors.Add(new CounsellorProfile(user.Id, null, request.YearsExperience,
                    request.Specialisations, request.Languages, request.VerificationDocumentUrls));
                db.RefreshTokens.Add(new RefreshToken(user.Id, refreshValue, DateTimeOffset.UtcNow.AddDays(refreshDays)));
                await db.SaveChangesAsync(cancellationToken);
                var accessToken = tokens.CreateAccessToken(user, [MirageRoles.Counsellor]);
                return ApiResults.Ok(context,
                    new AuthResponse(accessToken.Token, accessToken.ExpiresAt, refreshValue),
                    "Registration submitted — your account will be reviewed by an administrator before you appear publicly.");
            });
        }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException
                                                  { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]>
                {
                    ["DuplicateUserName"] = ["An account with this email already exists."]
                },
                "Registration validation failed.");
        }
    }

    private static async Task<IResult> RegisterMentor(RegisterMentorRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        IPasswordHasher<ApplicationUser> passwordHasher,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "Email is required."));
        if (!new EmailAddressAttribute().IsValid(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "A valid email address is required."));
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return EndpointHelpers.ValidationProblem(context, ("displayName", "Display name is required."));
        if (!EndpointHelpers.IsAtLeast18(request.DateOfBirth))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Mentors must be at least 18 years old."));
        if (!EndpointHelpers.IsPlausibleBirthDate(request.DateOfBirth))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Please enter a valid date of birth."));
        if (request.YearsMarried < 1)
            return EndpointHelpers.ValidationProblem(context, ("yearsMarried", "At least 1 year of marriage is required."));
        if (string.IsNullOrWhiteSpace(request.Testimony))
            return EndpointHelpers.ValidationProblem(context, ("testimony", "A personal testimony is required."));

        var normalizedEmail = userManager.NormalizeEmail(request.Email.Trim());
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            UserName = request.Email.Trim(),
            NormalizedUserName = userManager.NormalizeName(request.Email.Trim()),
            EmailConfirmed = false,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };

        var passwordErrors = new List<IdentityError>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, request.Password);
            if (!result.Succeeded) passwordErrors.AddRange(result.Errors);
        }
        if (passwordErrors.Count > 0)
            return EndpointHelpers.ValidationProblem(context,
                passwordErrors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()),
                "Registration validation failed.");

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var mentorRoleId = await db.Roles.AsNoTracking()
            .Where(x => x.NormalizedName == MirageRoles.Mentor.ToUpperInvariant())
            .Select(x => x.Id)
            .SingleAsync(cancellationToken);

        var refreshValue = tokens.CreateRefreshToken();
        var refreshDays = RefreshTokenDays(context, configuration);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = mentorRoleId });
                db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth,
                    request.City, request.Country, request.Denomination, request.Bio));
                db.Mentors.Add(new MentorProfile(user.Id, request.YearsMarried, request.Testimony,
                    request.AreasOfGuidance, request.Languages));
                db.RefreshTokens.Add(new RefreshToken(user.Id, refreshValue, DateTimeOffset.UtcNow.AddDays(refreshDays)));
                await db.SaveChangesAsync(cancellationToken);
                var accessToken = tokens.CreateAccessToken(user, [MirageRoles.Mentor]);
                return ApiResults.Ok(context,
                    new AuthResponse(accessToken.Token, accessToken.ExpiresAt, refreshValue),
                    "Mentor registration completed successfully.");
            });
        }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException
                                                  { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]>
                {
                    ["DuplicateUserName"] = ["An account with this email already exists."]
                },
                "Registration validation failed.");
        }
    }

    private static async Task<IResult> Login(LoginRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Invalid email or password.");
        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Invalid email or password.");
        }
        await userManager.ResetAccessFailedCountAsync(user);
        var roles = await userManager.GetRolesAsync(user);
        return ApiResults.Ok(context,
            await IssueTokens(user, roles, db, tokens, configuration, context, cancellationToken),
            "Login completed successfully.");
    }

    // Google Identity Services ID-token flow — the frontend never sees a client secret, it just
    // hands us the ID token Google issued after the user picked an account. We verify it against
    // our own Client ID and trust Google's own email verification, since that's what "Sign in
    // with Google" is vouching for. Works for both first-time signup and returning login: an
    // existing account by email just gets tokens issued; a new one is created with a minimal
    // profile that IsProfileComplete gates until they fill in the rest (see CompleteProfile below).
    private static async Task<IResult> GoogleAuth(GoogleAuthRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, IMemoryCache cache, MirageDbContext db, TokenService tokens,
        IConfiguration configuration, IEmailService emailService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            return EndpointHelpers.ValidationProblem(context, ("idToken", "Google ID token is required."));

        // Every platform Google issues tokens for has its own client ID, and the `aud` claim carries
        // whichever one requested the token — so the web client ID alone would reject a perfectly
        // valid sign-in from the iOS or Android app. All configured IDs are accepted; each is a
        // public identifier, not a secret, and Google's signature check is what actually
        // authenticates the token.
        var clientIds = GoogleClientIds(configuration);
        if (clientIds.Length == 0)
            return EndpointHelpers.Problem(context, StatusCodes.Status500InternalServerError,
                "Google sign-in unavailable", "Google sign-in is not configured on this deployment.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = clientIds });
        }
        catch (InvalidJwtException)
        {
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "This Google sign-in could not be verified.");
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || !payload.EmailVerified)
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Your Google account's email must be verified.");

        var existingUser = await userManager.FindByEmailAsync(payload.Email);
        if (existingUser is not null)
        {
            if (!existingUser.IsActive)
                return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                    "Authentication failed", "This account is unavailable.");

            // A Google sign-in proves ownership of the address just as strongly as clicking a
            // confirmation link, so both verification flags are healed here — this also unblocks
            // older Google-created accounts whose profiles predate starting out verified and were
            // stuck unable to like/match (the Like gate requires profile.IsVerified).
            if (!existingUser.EmailConfirmed)
            {
                existingUser.EmailConfirmed = true;
                await userManager.UpdateAsync(existingUser);
            }
            var existingProfile = await db.Profiles.SingleOrDefaultAsync(
                x => x.UserId == existingUser.Id, cancellationToken);
            if (existingProfile is null)
            {
                var displayName = string.IsNullOrWhiteSpace(payload.Name)
                    ? payload.Email.Split('@')[0]
                    : payload.Name;
                existingProfile = new UserProfile(existingUser.Id, displayName, avatarUrl: null,
                    signupIpAddress: GetClientIpAddress(context));
                db.Profiles.Add(existingProfile);
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (existingProfile.IsProfileComplete && !existingProfile.IsVerified)
            {
                existingProfile.Verify();
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (existingProfile is not null && !existingProfile.IsProfileComplete && existingProfile.IsVerified)
            {
                existingProfile.RevokeVerification();
                await db.SaveChangesAsync(cancellationToken);
            }

            var existingRoles = await userManager.GetRolesAsync(existingUser);
            return ApiResults.Ok(context,
                await IssueTokens(existingUser, existingRoles, db, tokens, configuration, context, cancellationToken),
                "Signed in with Google successfully.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = payload.Email,
            NormalizedEmail = userManager.NormalizeEmail(payload.Email),
            UserName = payload.Email,
            NormalizedUserName = userManager.NormalizeName(payload.Email),
            EmailConfirmed = true,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                var roleId = await cache.GetOrCreateAsync(IdentityCacheKeys.DefaultUserRoleId, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return await db.Roles.AsNoTracking()
                        .Where(x => x.NormalizedName == MirageRoles.User.ToUpperInvariant())
                        .Select(x => x.Id)
                        .SingleAsync(cancellationToken);
                });

                var displayName = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email.Split('@')[0] : payload.Name;
                var refreshValue = tokens.CreateRefreshToken();
                var refreshDays = RefreshTokenDays(context, configuration);
                var accessToken = tokens.CreateAccessToken(user, [MirageRoles.User]);

                // Do not trust the Google account avatar as a dating profile photo. It may be a
                // logo, illustration, group photo, or stale image and has not passed our
                // server-side face detector. CompleteProfile requires a fresh validated upload.
                var profile = new UserProfile(user.Id, displayName, avatarUrl: null,
                    signupIpAddress: GetClientIpAddress(context));
                // Email ownership is verified by Google, but the Mirage profile is deliberately
                // not verified until mandatory details and a server-validated face photo are saved.

                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
                db.Profiles.Add(profile);
                db.RefreshTokens.Add(new RefreshToken(user.Id, refreshValue, DateTimeOffset.UtcNow.AddDays(refreshDays)));
                await db.SaveChangesAsync(cancellationToken);

                if (await emailService.SendWelcomeEmailAsync(user.Email!, displayName, cancellationToken: cancellationToken))
                {
                    user.WelcomeEmailSentAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }

                return ApiResults.Ok(context, new AuthResponse(accessToken.Token, accessToken.ExpiresAt, refreshValue),
                    "Account created with Google successfully.");
            });
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                 { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]> { ["DuplicateUserName"] = ["An account with this email already exists."] },
                "Registration validation failed.");
        }
    }

    private static async Task<IResult> Refresh(RefreshRequest request, HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, TokenService tokens, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var hash = RefreshToken.ComputeHash(request.RefreshToken);
        // Deliberately not filtered on RevokedAt: a token rotated seconds ago has to be told apart
        // from one that was never valid, so the row is loaded first and judged below.
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(
            x => x.TokenHash == hash && x.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
        var grace = TimeSpan.FromSeconds(configuration.GetValue("Jwt:RefreshRotationGraceSeconds", 60));
        if (existing is null || (existing.RevokedAt is not null && !existing.IsWithinRotationGrace(grace)))
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Invalid refresh token.");
        existing.Revoke();
        var user = await userManager.FindByIdAsync(existing.UserId.ToString());
        if (user is null || !user.IsActive)
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "User is unavailable.");
        var roles = await userManager.GetRolesAsync(user);
        return ApiResults.Ok(context,
            await IssueTokens(user, roles, db, tokens, configuration, context, cancellationToken),
            "Token refreshed successfully.");
    }

    private static async Task<IResult> Logout(RefreshRequest request, HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var hash = RefreshToken.ComputeHash(request.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(
            x => x.UserId == userId && x.TokenHash == hash && x.RevokedAt == null,
            cancellationToken);
        if (token is null) return EndpointHelpers.NotFound(context, "Active session was not found.");

        token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { token.Id }, "Logout completed successfully.");
    }

    private static async Task<IResult> LogoutAll(HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { revokedSessions = tokens.Count },
            "All sessions have been logged out successfully.");
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, IEmailService emailService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return EndpointHelpers.ValidationProblem(context,
                ("password", "Current and new passwords are required."));

        var user = await userManager.FindByIdAsync(context.User.GetUserId().ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User account was not found.");
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return EndpointHelpers.ValidationProblem(context,
                result.Errors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()),
                "Password change failed.");

        var revokedSessions = await RevokeAllSessionsAsync(user.Id, db, cancellationToken);

        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == user.Id).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        if (user.Email is not null)
            await emailService.SendPasswordChangedEmailAsync(user.Email, displayName ?? "there", cancellationToken);

        return ApiResults.Ok(context, new { revokedSessions },
            "Password changed successfully. Sign in again on all devices.");
    }

    private static async Task<IResult> VerifyPassword(VerifyPasswordRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager)
    {
        if (string.IsNullOrEmpty(request.Password))
            return EndpointHelpers.ValidationProblem(context, ("password", "Account password is required."));
        var user = await userManager.FindByIdAsync(context.User.GetUserId().ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User account was not found.");
        if (!await userManager.CheckPasswordAsync(user, request.Password))
            return EndpointHelpers.ValidationProblem(context, ("password", "Account password is incorrect."));
        return ApiResults.Ok(context, new { verified = true }, "Account password verified.");
    }

    private static async Task<IResult> DeactivateAccount(DeactivateAccountRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, IEmailService emailService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return EndpointHelpers.ValidationProblem(context, ("currentPassword", "Current password is required."));

        var user = await userManager.FindByIdAsync(context.User.GetUserId().ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User account was not found.");
        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
            return EndpointHelpers.ValidationProblem(context, ("currentPassword", "Current password is incorrect."));

        user.IsActive = false;
        await userManager.UpdateAsync(user);
        await RevokeAllSessionsAsync(user.Id, db, cancellationToken);

        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == user.Id).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        if (user.Email is not null)
            await emailService.SendAccountClosedEmailAsync(user.Email, displayName ?? "there", permanent: false, cancellationToken);

        return ApiResults.Ok(context, new { }, "Your account has been deactivated. Contact support to reactivate it.");
    }

    private static async Task<IResult> DeleteAccount(DeleteAccountRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, IEmailService emailService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return EndpointHelpers.ValidationProblem(context, ("currentPassword", "Current password is required."));

        var user = await userManager.FindByIdAsync(context.User.GetUserId().ToString());
        if (user is null) return EndpointHelpers.NotFound(context, "User account was not found.");
        if (!await userManager.CheckPasswordAsync(user, request.CurrentPassword))
            return EndpointHelpers.ValidationProblem(context, ("currentPassword", "Current password is incorrect."));

        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == user.Id).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var email = user.Email;

        // Hard-deleting the ApplicationUser row isn't safe — matches, messages, payments, and
        // counselling sessions all Restrict-FK to the user, so other members' history would break.
        // Instead we deactivate the login and scrub the profile of anything personally identifying.
        user.IsActive = false;
        user.IsDeleted = true;
        await userManager.UpdateAsync(user);

        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        profile?.ScrubPersonalData();

        await RevokeAllSessionsAsync(user.Id, db, cancellationToken);
        AdminEndpoints.InvalidateAdminReads();

        if (email is not null)
            await emailService.SendAccountClosedEmailAsync(email, displayName ?? "there", permanent: true, cancellationToken);

        return ApiResults.Ok(context, new { }, "Your account has been deleted.");
    }

    private static async Task<int> RevokeAllSessionsAsync(Guid userId, MirageDbContext db, CancellationToken cancellationToken)
    {
        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return tokens.Count;
    }

    private static async Task<IResult> ForgotPassword(ForgotPasswordRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, IEmailService emailService,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "Email is required."));

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user?.Email is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var displayName = await db.Profiles.AsNoTracking()
                .Where(x => x.UserId == user.Id).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
            var appUrl = configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
            var resetUrl = $"{appUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";
            await emailService.SendPasswordResetEmailAsync(user.Email, displayName ?? "there", resetUrl, cancellationToken);
        }

        // Always report success, whether or not the email is registered, so this endpoint can't be
        // used to enumerate accounts.
        return ApiResults.Ok(context, new { }, "If an account exists for that email, a reset link has been sent.");
    }

    private static async Task<IResult> ResetPassword(ResetPasswordRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, IEmailService emailService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
            return EndpointHelpers.ValidationProblem(context,
                ("password", "Email, token, and new password are required."));

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]> { ["token"] = ["This reset link is invalid or has expired."] },
                "Password reset failed.");

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
            return EndpointHelpers.ValidationProblem(context,
                result.Errors.GroupBy(x => x.Code)
                    .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()),
                "Password reset failed.");

        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == user.Id && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.Revoke();
        await db.SaveChangesAsync(cancellationToken);

        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == user.Id).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        if (user.Email is not null)
            await emailService.SendPasswordChangedEmailAsync(user.Email, displayName ?? "there", cancellationToken);

        return ApiResults.Ok(context, new { }, "Password reset successfully. Sign in with your new password.");
    }

    private static async Task<IResult> ConfirmEmail(ConfirmEmailRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
            return EndpointHelpers.ValidationProblem(context, ("token", "Email and token are required."));

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]> { ["token"] = ["This confirmation link is invalid or has expired."] },
                "Email confirmation failed.");

        if (user.EmailConfirmed)
            return ApiResults.Ok(context, new { }, "Your email is already confirmed.");

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return EndpointHelpers.ValidationProblem(context,
                new Dictionary<string, string[]> { ["token"] = ["This confirmation link is invalid or has expired."] },
                "Email confirmation failed.");

        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);
        if (profile is not null && !profile.IsVerified)
        {
            profile.Verify();
            await db.SaveChangesAsync(cancellationToken);
        }

        return ApiResults.Ok(context, new { }, "Your email has been confirmed. You can now like, match, and chat with other members.");
    }

    private static async Task<IResult> ResendConfirmation(ResendConfirmationEmailRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager, MirageDbContext db, IEmailService emailService,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return EndpointHelpers.ValidationProblem(context, ("email", "Email is required."));

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not null && !user.EmailConfirmed && user.Email is not null)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var displayName = await db.Profiles.AsNoTracking()
                .Where(x => x.UserId == user.Id).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
            var appUrl = configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
            var confirmUrl = $"{appUrl}/confirm-email?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token)}";
            await emailService.SendEmailConfirmationAsync(user.Email, displayName ?? "there", confirmUrl, cancellationToken);
        }

        // Always report success, whether or not the email is registered or already confirmed, so
        // this endpoint can't be used to enumerate accounts.
        return ApiResults.Ok(context, new { }, "If an account exists and needs confirmation, a new link has been sent.");
    }

    private static async Task<IResult> GetSessions(HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var sessions = await db.RefreshTokens.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AccountSessionResponse(
                x.Id,
                x.CreatedAt,
                x.ExpiresAt,
                x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow))
            .Take(100)
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, sessions, "Account sessions retrieved successfully.");
    }

    private static async Task<AuthResponse> IssueTokens(ApplicationUser user, IEnumerable<string> roles,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, HttpContext context,
        CancellationToken cancellationToken)
    {
        user.LastLoginAt = DateTimeOffset.UtcNow;
        // They came back, so the "we miss you" series has done its job — clear it so a future
        // lapse starts from the first email again rather than resuming a spent quota.
        user.ReEngagementEmailCount = 0;
        user.LastReEngagementEmailAt = null;
        var access = tokens.CreateAccessToken(user, roles);
        var refreshValue = tokens.CreateRefreshToken();
        var refreshDays = RefreshTokenDays(context, configuration);
        db.RefreshTokens.Add(new RefreshToken(user.Id, refreshValue, DateTimeOffset.UtcNow.AddDays(refreshDays)));
        await db.SaveChangesAsync(cancellationToken);
        return new AuthResponse(access.Token, access.ExpiresAt, refreshValue);
    }

    /// <summary>
    /// How long a refresh token issued on this request should live. Native apps are held in the
    /// hand, protected by the device lock screen, and expected to stay signed in the way every
    /// other phone app does; a browser session on a possibly shared machine is not. The client
    /// declares itself with X-Client-Platform, and anything that does not say "mobile" (including
    /// a caller that lies by omission) falls back to the shorter web lifetime.
    /// </summary>
    private static int RefreshTokenDays(HttpContext context, IConfiguration configuration)
    {
        var webDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);
        var platform = context.Request.Headers["X-Client-Platform"].ToString();
        return string.Equals(platform, "mobile", StringComparison.OrdinalIgnoreCase)
            ? configuration.GetValue("Jwt:MobileRefreshTokenDays", 365)
            : webDays;
    }

    // Prefers the frontend-supplied X-Client-IP (populated from a browser-side IP lookup), since
    // ForwardedHeadersMiddleware's view of RemoteIpAddress isn't reliable behind every load
    // balancer topology. This is a best-effort signal for location prefill/consistency, not a
    // hard security control — a direct API caller can send any value here.
    private static string? GetClientIpAddress(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Client-IP", out var header))
        {
            var candidate = header.ToString().Trim();
            if (IPAddress.TryParse(candidate, out _)) return candidate;
        }
        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Every Google client ID this deployment accepts an ID token from.
    /// </summary>
    /// <remarks>
    /// Google:ClientId is the web client used by the SPA. Google:IosClientId and
    /// Google:AndroidClientId are the native apps, which mint tokens with their own `aud` claim.
    /// Also accepts a comma-separated Google:AdditionalClientIds for anything added later without a
    /// code change. Order does not matter — ValidateAsync accepts a token matching any entry.
    /// </remarks>
    private static string[] GoogleClientIds(IConfiguration configuration)
    {
        var configured = new[]
        {
            configuration["Google:ClientId"],
            configuration["Google:IosClientId"],
            configuration["Google:AndroidClientId"],
        }.AsEnumerable();

        var additional = configuration["Google:AdditionalClientIds"];
        if (!string.IsNullOrWhiteSpace(additional))
            configured = configured.Concat(additional.Split(',', StringSplitOptions.RemoveEmptyEntries));

        return [.. configured
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.Ordinal)];
    }

    private static (string Field, string Error)[] Validate(RegisterRequest request)
    {
        var errors = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(request.Email)) errors.Add(("email", "Email is required."));
        else if (!new EmailAddressAttribute().IsValid(request.Email))
            errors.Add(("email", "A valid email address is required."));
        if (string.IsNullOrWhiteSpace(request.DisplayName)) errors.Add(("displayName", "Display name is required."));
        if (!EndpointHelpers.IsAtLeast18(request.DateOfBirth))
            errors.Add(("dateOfBirth", "Users must be at least 18 years old."));
        else if (!EndpointHelpers.IsPlausibleBirthDate(request.DateOfBirth))
            errors.Add(("dateOfBirth", "Please enter a valid date of birth."));
        if (string.IsNullOrWhiteSpace(request.City)) errors.Add(("city", "City is required."));
        if (request.Sex is null) errors.Add(("sex", "Select your sex."));
        if (string.IsNullOrWhiteSpace(request.ConfirmPassword))
            errors.Add(("confirmPassword", "Please confirm your password."));
        else if (request.Password != request.ConfirmPassword)
            errors.Add(("confirmPassword", "Passwords do not match."));
        if (!string.IsNullOrWhiteSpace(request.Denomination) &&
            !Enum.TryParse<ChristianDenomination>(request.Denomination, ignoreCase: true, out _))
            errors.Add(("denomination", "Select a valid denomination."));
        return errors.ToArray();
    }

}
