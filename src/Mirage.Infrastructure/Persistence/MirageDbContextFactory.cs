using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Mirage.Infrastructure.Persistence;

public sealed class MirageDbContextFactory : IDesignTimeDbContextFactory<MirageDbContext>
{
    public MirageDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var apiDirectory = ResolveApiDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = PostgresConnectionString.FromConfiguration(configuration);

        var options = new DbContextOptionsBuilder<MirageDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new MirageDbContext(options);
    }

    private static string ResolveApiDirectory()
    {
        var current = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            current,
            Path.Combine(current, "src", "Mirage.Api"),
            Path.Combine(current, "..", "Mirage.Api"),
            Path.Combine(current, "..", "..", "Mirage.Api")
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "appsettings.json")))
            ?? current;
    }
}
