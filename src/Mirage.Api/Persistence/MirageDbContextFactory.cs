using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Persistence;

public sealed class MirageDbContextFactory : IDesignTimeDbContextFactory<MirageDbContext>
{
    public MirageDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<Program>(optional: true)
            .Build();

        var connectionString = PostgresConnectionString.FromConfiguration(configuration);
        var options = new DbContextOptionsBuilder<MirageDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(MirageDbContext).Assembly.FullName))
            .Options;

        return new MirageDbContext(options);
    }
}
