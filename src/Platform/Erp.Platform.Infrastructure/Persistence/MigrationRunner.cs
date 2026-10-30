using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Erp.Platform.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseNpgsql("Host=127.0.0.1;Database=design_only;Username=design_only").Options);
}

public static class MigrationRunner
{
    public static async Task ApplyAsync(string connectionString, CancellationToken token = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using (var guard = new NpgsqlCommand("SELECT current_user<>'erp_runtime' AND NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname=current_user", connection))
            if (await guard.ExecuteScalarAsync(token) is not true)
                throw new InvalidOperationException("Use the separate non-superuser migration identity.");
        await using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection).Options);
        // One controlled migration host, never every API/Worker replica.
        await db.Database.MigrateAsync(token);
    }
}
