using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mirage.Infrastructure.Persistence;

public sealed class MirageDbContextFactory : IDesignTimeDbContextFactory<MirageDbContext>
{
    public MirageDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = "Host=localhost;Port=5432;Database=mirage;Username=mirage;Password=mirage";

        var options = new DbContextOptionsBuilder<MirageDbContext>()
            .UseNpgsql(PostgresConnectionString.Build(connectionString))
            .Options;
        return new MirageDbContext(options);
    }
}
