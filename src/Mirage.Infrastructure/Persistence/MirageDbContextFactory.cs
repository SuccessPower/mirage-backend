using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Mirage.Infrastructure.Persistence;

public sealed class MirageDbContextFactory : IDesignTimeDbContextFactory<MirageDbContext>
{
    public MirageDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var connectionString = PostgresConnectionString.FromConfiguration(configuration);

        var options = new DbContextOptionsBuilder<MirageDbContext>()
            .UseNpgsql(PostgresConnectionString.Build(connectionString))
            .Options;
        return new MirageDbContext(options);
    }
}
