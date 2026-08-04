using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;

namespace Mirage.Infrastructure.Persistence;

public static class DatabaseInitialiser
{
    private const long MigrationLockId = 6_141_726_503_726_643_145;

    public static async Task InitialiseDatabaseAsync(
        this IHost app,
        bool forceMigrations = false,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!forceMigrations && !configuration.GetValue("Database:ApplyMigrationsOnStartup", true)) return;

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MirageDbContext>>();
        var db = scope.ServiceProvider.GetRequiredService<MirageDbContext>();
        logger.LogInformation("Acquiring PostgreSQL migration lock.");

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_lock({MigrationLockId});",
                cancellationToken);

            logger.LogInformation("Applying Mirage database migrations.");
            await db.Database.MigrateAsync(cancellationToken);

            await SeedRolesAsync(scope.ServiceProvider, cancellationToken);
            await SeedSuperAdminAsync(scope.ServiceProvider, configuration, cancellationToken);
            await SeedSystemAccountAsync(scope.ServiceProvider, db, cancellationToken);
            await SyncChurchDirectoryAsync(db, logger, cancellationToken);
            await SeedCompanionPromptsAsync(db, cancellationToken);
            logger.LogInformation("Database migration and role initialization completed.");
        }
        finally
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"SELECT pg_advisory_unlock({MigrationLockId});",
                    CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    public static async Task WarmDatabaseCachesAsync(this IHost app, CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var db = scope.ServiceProvider.GetRequiredService<MirageDbContext>();
        await cache.GetOrCreateAsync(IdentityCacheKeys.DefaultUserRoleId, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await db.Roles.AsNoTracking()
                .Where(role => role.NormalizedName == MirageRoles.User.ToUpperInvariant())
                .Select(role => role.Id)
                .SingleAsync(cancellationToken);
        });
    }

    // Promotes already-registered accounts to PlatformAdmin on startup, driven by config
    // (e.g. SuperAdmin__Email env var) rather than any seeded/default credentials — there is
    // no built-in admin account. Register normally first, then set this to your own email.
    // Accepts a comma-separated list so multiple admins can be granted from one env var; each
    // account must already exist (the seeder never creates a user, only grants the role).
    private static async Task SeedSuperAdminAsync(IServiceProvider services, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var raw = configuration["SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(raw)) return;

        var emails = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (emails.Length == 0) return;

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var email in emails)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null) continue;

            if (!await userManager.IsInRoleAsync(user, MirageRoles.PlatformAdmin))
                await userManager.AddToRoleAsync(user, MirageRoles.PlatformAdmin);
        }
    }

    // Runs on every startup (not just when someone remembers to click the admin "seed churches"
    // button) so denomination fixes and retired churches always take effect on deploy — this is
    // what actually clears out churches dropped from the curated list, since a manual button is
    // easy to forget to press. No PlatformAdmin context exists at startup, so brand-new churches
    // from the JSON are NOT auto-created here (that still needs a human owner via the admin
    // button) — only denomination corrections and retiring dropped churches run automatically.
    private static async Task SyncChurchDirectoryAsync(MirageDbContext db, ILogger logger,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SeedData", "nigerian-churches.json");
        var result = await ChurchDirectorySync.SyncAsync(db, path, actorId: null, cancellationToken);
        if (result.DenominationsUpdated > 0 || result.Retired > 0)
            logger.LogInformation(
                "Church directory sync: updated {DenominationsUpdated} denomination(s), retired {Retired} church(es).",
                result.DenominationsUpdated, result.Retired);
    }

    // Seeds the "Mirage Team" account used to author automatic birthday/anniversary celebration
    // posts and comments (CelebrationPostService) — a real Profiles row so the existing testimonial
    // author-lookup joins keep working unmodified. IsActive stays false and the password is a
    // random value discarded immediately: no one is meant to sign in as this account.
    private static async Task SeedSystemAccountAsync(IServiceProvider services, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = SystemAccounts.MirageTeamEmail.ToUpperInvariant();
        if (await db.Users.AsNoTracking().AnyAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken))
            return;

        var passwordHasher = services.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = SystemAccounts.MirageTeamEmail,
            NormalizedEmail = normalizedEmail,
            UserName = SystemAccounts.MirageTeamEmail,
            NormalizedUserName = normalizedEmail,
            EmailConfirmed = true,
            LockoutEnabled = true,
            IsActive = false,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

        db.Users.Add(user);
        db.Profiles.Add(new UserProfile(user.Id, SystemAccounts.MirageTeamDisplayName, avatarUrl: null));
        await db.SaveChangesAsync(cancellationToken);
    }

    // One-time seed of the Companion question bank — never re-runs once any prompt exists, so
    // admins can safely edit/retire individual prompts later without this overwriting them.
    private static async Task SeedCompanionPromptsAsync(MirageDbContext db, CancellationToken cancellationToken)
    {
        if (await db.CompanionPrompts.AsNoTracking().AnyAsync(cancellationToken)) return;

        (string Text, string Category, CompanionCadence Cadence)[] prompts =
        [
            // Connection — Weekly
            ("What made you feel seen by your partner this week?", "Connection", CompanionCadence.Weekly),
            ("When did you feel closest to each other recently?", "Connection", CompanionCadence.Weekly),
            ("What is a small moment together this week that you don't want to forget?", "Connection", CompanionCadence.Weekly),
            ("How did your partner make you smile this week?", "Connection", CompanionCadence.Weekly),
            ("What's one thing you wish you had more time for together?", "Connection", CompanionCadence.Weekly),
            ("When did you feel most at peace with each other lately?", "Connection", CompanionCadence.Weekly),
            // Communication — Bi-weekly
            ("Where do we need more honesty, tenderness, or structure?", "Communication", CompanionCadence.BiWeekly),
            ("Is there something you've been holding back from saying?", "Communication", CompanionCadence.BiWeekly),
            ("What's a conversation we keep avoiding?", "Communication", CompanionCadence.BiWeekly),
            ("How can I listen to you better?", "Communication", CompanionCadence.BiWeekly),
            ("What do you need from me that you haven't asked for?", "Communication", CompanionCadence.BiWeekly),
            ("When do you feel most understood by me?", "Communication", CompanionCadence.BiWeekly),
            // Gratitude — Daily
            ("What is one thing you are grateful for about your partner today?", "Gratitude", CompanionCadence.Daily),
            ("What small act of love did you notice today?", "Gratitude", CompanionCadence.Daily),
            ("What do you appreciate most about how your partner showed up today?", "Gratitude", CompanionCadence.Daily),
            ("Who or what made your day better because of your partner?", "Gratitude", CompanionCadence.Daily),
            ("What's a quality in your partner you're thankful for right now?", "Gratitude", CompanionCadence.Daily),
            ("What moment today are you thankful you shared together?", "Gratitude", CompanionCadence.Daily),
            // Growth — Monthly
            ("Are we growing together or apart? What does growth look like?", "Growth", CompanionCadence.Monthly),
            ("What have we learned about each other this month?", "Growth", CompanionCadence.Monthly),
            ("How have you changed in this relationship, for better or worse?", "Growth", CompanionCadence.Monthly),
            ("What's a habit we could build together this month?", "Growth", CompanionCadence.Monthly),
            ("Where do you want us to be a year from now?", "Growth", CompanionCadence.Monthly),
            ("What lesson from a hard season are we still carrying?", "Growth", CompanionCadence.Monthly),
            // Faith — Weekly
            ("How is God shaping our relationship right now?", "Faith", CompanionCadence.Weekly),
            ("What's a prayer you've been carrying for us?", "Faith", CompanionCadence.Weekly),
            ("How can we grow spiritually together this season?", "Faith", CompanionCadence.Weekly),
            ("What scripture or truth has encouraged you lately?", "Faith", CompanionCadence.Weekly),
            ("Where do you sense God's faithfulness in our story?", "Faith", CompanionCadence.Weekly),
            ("How can I support your walk with God better?", "Faith", CompanionCadence.Weekly),
            // Conflict — Bi-weekly
            ("What's something from a recent disagreement we haven't fully resolved?", "Conflict", CompanionCadence.BiWeekly),
            ("How can we argue in a way that still honors each other?", "Conflict", CompanionCadence.BiWeekly),
            ("What triggers you most in conflict, and why?", "Conflict", CompanionCadence.BiWeekly),
            ("What do you need from me right after we've argued?", "Conflict", CompanionCadence.BiWeekly),
            ("Is there an apology, on either side, still owed?", "Conflict", CompanionCadence.BiWeekly),
            ("What pattern in our conflicts would you like us to break?", "Conflict", CompanionCadence.BiWeekly),
            // Trust — Monthly
            ("What builds your trust in me, and what shakes it?", "Trust", CompanionCadence.Monthly),
            ("Is there anything unspoken that's affecting your trust right now?", "Trust", CompanionCadence.Monthly),
            ("How has our trust grown since we started this journey?", "Trust", CompanionCadence.Monthly),
            ("What would help you feel more secure in us?", "Trust", CompanionCadence.Monthly),
            ("Where do you feel most safe being fully yourself with me?", "Trust", CompanionCadence.Monthly),
            ("What promise matters most to you right now?", "Trust", CompanionCadence.Monthly),
            // Future — Monthly
            ("What dream for our future haven't we talked about lately?", "Future", CompanionCadence.Monthly),
            ("What does our ideal year from now look like?", "Future", CompanionCadence.Monthly),
            ("What are we building toward together right now?", "Future", CompanionCadence.Monthly),
            ("What legacy do we want to leave as a couple?", "Future", CompanionCadence.Monthly),
            ("What's one goal we should chase together this season?", "Future", CompanionCadence.Monthly),
            ("How do you picture us growing old together?", "Future", CompanionCadence.Monthly),
            // Fun — Weekly
            ("What's something fun we haven't done in a while?", "Fun", CompanionCadence.Weekly),
            ("What made you laugh together recently?", "Fun", CompanionCadence.Weekly),
            ("If we had a free weekend, what would you want to do together?", "Fun", CompanionCadence.Weekly),
            ("What's a memory that still makes you both laugh?", "Fun", CompanionCadence.Weekly),
            ("What new adventure should we try together?", "Fun", CompanionCadence.Weekly),
            ("What's your favorite way to unwind together?", "Fun", CompanionCadence.Weekly),
            // Intimacy — Daily
            ("What made you feel emotionally close to me today?", "Intimacy", CompanionCadence.Daily),
            ("How can I love you better tomorrow?", "Intimacy", CompanionCadence.Daily),
            ("What's something you needed today that you didn't ask for?", "Intimacy", CompanionCadence.Daily),
            ("When did you feel most cherished today?", "Intimacy", CompanionCadence.Daily),
            ("What's one way I can show you affection this week?", "Intimacy", CompanionCadence.Daily),
            ("What does feeling loved look like for you right now?", "Intimacy", CompanionCadence.Daily),
        ];

        db.CompanionPrompts.AddRange(prompts.Select(p => new CompanionPrompt(p.Text, p.Category, p.Cadence)));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedRolesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in MirageRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (!result.Succeeded)
                {
                    var errors = string.Join("; ", result.Errors.Select(error => error.Description));
                    throw new InvalidOperationException($"Failed to create role '{role}': {errors}");
                }
            }
        }
    }
}
