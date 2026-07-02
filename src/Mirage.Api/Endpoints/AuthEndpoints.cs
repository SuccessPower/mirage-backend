using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Mirage.Api.Contracts;
using Mirage.Api.Middleware;
using Mirage.Api.Security;
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
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapPost("/logout-all", LogoutAll).RequireAuthorization();
        group.MapPost("/change-password", ChangePassword).RequireAuthorization();
        group.MapGet("/sessions", GetSessions).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> Register(RegisterRequest request, HttpContext context,
        UserManager<ApplicationUser> userManager,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IMemoryCache cache,
        MirageDbContext db, TokenService tokens, IConfiguration configuration, ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Length > 0) return EndpointHelpers.ValidationProblem(context, errors);

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
            ConcurrencyStamp = Guid.NewGuid().ToString()
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
                var refreshDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);
                var refreshToken = new RefreshToken(
                    user.Id,
                    refreshValue,
                    DateTimeOffset.UtcNow.AddDays(refreshDays));
                var accessToken = tokens.CreateAccessToken(user, [MirageRoles.User]);

                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleId });
                db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth, request.City,
                    request.Country, request.Denomination, request.Intent, request.Bio, request.Sex,
                    request.RelationshipStatus));
                db.RefreshTokens.Add(refreshToken);
                await db.SaveChangesAsync(cancellationToken);

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
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Counsellors must be at least 18 years old."));
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
        var refreshDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = counsellorRoleId });
                db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth,
                    request.City, request.Country, request.Denomination, RelationshipIntent.Marriage, request.Bio));
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
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Counsellors must be at least 18 years old."));
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
        var refreshDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = counsellorRoleId });
                db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth,
                    request.City, request.Country, request.Denomination, RelationshipIntent.Marriage, request.Bio));
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
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))
            return EndpointHelpers.ValidationProblem(context, ("dateOfBirth", "Mentors must be at least 18 years old."));
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
        var refreshDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.Users.Add(user);
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = mentorRoleId });
                db.Profiles.Add(new UserProfile(user.Id, request.DisplayName, request.DateOfBirth,
                    request.City, request.Country, request.Denomination, RelationshipIntent.Marriage, request.Bio));
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
            await IssueTokens(user, roles, db, tokens, configuration, cancellationToken),
            "Login completed successfully.");
    }

    private static async Task<IResult> Refresh(RefreshRequest request, HttpContext context, MirageDbContext db,
        UserManager<ApplicationUser> userManager, TokenService tokens, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var hash = RefreshToken.ComputeHash(request.RefreshToken);
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(
            x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow,
            cancellationToken);
        if (existing is null)
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "Invalid refresh token.");
        existing.Revoke();
        var user = await userManager.FindByIdAsync(existing.UserId.ToString());
        if (user is null || !user.IsActive)
            return EndpointHelpers.Problem(context, StatusCodes.Status401Unauthorized,
                "Authentication failed", "User is unavailable.");
        var roles = await userManager.GetRolesAsync(user);
        return ApiResults.Ok(context,
            await IssueTokens(user, roles, db, tokens, configuration, cancellationToken),
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
        UserManager<ApplicationUser> userManager, MirageDbContext db, CancellationToken cancellationToken)
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

        var tokens = await db.RefreshTokens
            .Where(x => x.UserId == user.Id && x.RevokedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { revokedSessions = tokens.Count },
            "Password changed successfully. Sign in again on all devices.");
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
        MirageDbContext db, TokenService tokens, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var access = tokens.CreateAccessToken(user, roles);
        var refreshValue = tokens.CreateRefreshToken();
        var refreshDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);
        db.RefreshTokens.Add(new RefreshToken(user.Id, refreshValue, DateTimeOffset.UtcNow.AddDays(refreshDays)));
        await db.SaveChangesAsync(cancellationToken);
        return new AuthResponse(access.Token, access.ExpiresAt, refreshValue);
    }

    private static (string Field, string Error)[] Validate(RegisterRequest request)
    {
        var errors = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(request.Email)) errors.Add(("email", "Email is required."));
        else if (!new EmailAddressAttribute().IsValid(request.Email))
            errors.Add(("email", "A valid email address is required."));
        if (string.IsNullOrWhiteSpace(request.DisplayName)) errors.Add(("displayName", "Display name is required."));
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))
            errors.Add(("dateOfBirth", "Users must be at least 18 years old."));
        if (string.IsNullOrWhiteSpace(request.City)) errors.Add(("city", "City is required."));
        return errors.ToArray();
    }

}
