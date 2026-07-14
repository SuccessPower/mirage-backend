using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Mirage.Infrastructure.Persistence;

public static class PostgresConnectionString
{
    public static string Build(string raw, int maxPoolSize = 15)
    {
        NpgsqlConnectionStringBuilder builder;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "postgres" or "postgresql")
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port == -1 ? 5432 : uri.Port,
                Database = uri.AbsolutePath.TrimStart('/'),
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty
            };
        }
        else
        {
            builder = new NpgsqlConnectionStringBuilder(raw);
        }

        builder.SslMode = SslMode.Require;
        builder.Pooling = true;
        builder.MaxPoolSize = maxPoolSize;
        builder.MinPoolSize = 0;
        builder.ConnectionIdleLifetime = 300;
        builder.Timeout = 15;
        return builder.ConnectionString;
    }

    public static string FromConfiguration(IConfiguration configuration)
    {
        var raw = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Set DATABASE_URL or ConnectionStrings:Postgres.");
        return Build(raw, configuration.GetValue("Database:MaxPoolSize", 15));
    }
}
