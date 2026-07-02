using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
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

    // Promotes an already-registered account to PlatformAdmin on startup, driven by config
    // (e.g. SuperAdmin__Email env var) rather than any seeded/default credentials — there is
    // no built-in admin account. Register normally first, then set this to your own email.
    private static async Task SeedSuperAdminAsync(IServiceProvider services, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var email = configuration["SuperAdmin:Email"];
        if (string.IsNullOrWhiteSpace(email)) return;

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null) return;

        if (!await userManager.IsInRoleAsync(user, MirageRoles.PlatformAdmin))
            await userManager.AddToRoleAsync(user, MirageRoles.PlatformAdmin);
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
