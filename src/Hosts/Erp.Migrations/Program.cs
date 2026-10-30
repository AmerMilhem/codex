using Erp.Finance.Infrastructure;
using Erp.Platform.Infrastructure;
using Erp.Platform.Infrastructure.Persistence;

var connection = new EnvironmentSecrets().Get("ERP_MIGRATION_CONNECTION")
    ?? throw new InvalidOperationException("ERP_MIGRATION_CONNECTION must be supplied securely to this controlled runner.");
await MigrationRunner.ApplyAsync(connection);
await FinanceMigrations.ApplyAsync(connection);
Console.WriteLine("Foundation and Finance migrations applied.");
