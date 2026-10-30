using System.Diagnostics;
using Erp.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Platform.Infrastructure.Persistence;

public sealed class PostgresTransaction : IApplicationTransaction, IAsyncDisposable
{
    private readonly List<DbContext> contexts = [];
    public VerifiedContext Context { get; }
    public NpgsqlConnection Connection { get; }
    internal NpgsqlTransaction Transaction { get; }
    public PlatformDbContext Platform { get; }
    internal PostgresTransaction(VerifiedContext context, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Context = context; Connection = connection; Transaction = transaction;
        Platform = Enlist(c => new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c).Options));
    }
    public T Enlist<T>(Func<NpgsqlConnection, T> factory) where T : DbContext
    {
        var db = factory(Connection);
        if (!ReferenceEquals(db.Database.GetDbConnection(), Connection))
            throw new InvalidOperationException("Module context must share the unit of work connection.");
        db.Database.UseTransaction(Transaction); contexts.Add(db); return db;
    }
    internal async Task FlushAsync(CancellationToken token)
    {
        foreach (var db in contexts) await db.SaveChangesAsync(token);
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var db in contexts) await db.DisposeAsync();
    }
    public static PostgresTransaction Require(IApplicationTransaction transaction) => transaction as PostgresTransaction
        ?? throw new InvalidOperationException("PostgreSQL transaction required by this infrastructure adapter.");
}

public sealed class PostgresUnitOfWork(NpgsqlDataSource dataSource) : IUnitOfWorkExecutor
{
    public async Task<T> ExecuteAsync<T>(VerifiedContext context,
        Func<IApplicationTransaction, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var span = FoundationTelemetry.Source.StartActivity("database.unit_of_work", ActivityKind.Internal);
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await Sql.ExecuteAsync(connection, "SELECT set_config('erp.tenant_id', @tenant, true), set_config('erp.company_id', @company, true), set_config('erp.correlation_id', @correlation, true), set_config('erp.request_id', @request, true), set_config('erp.trace_id', @trace, true)",
                cancellationToken, ("tenant", context.TenantId.ToString()), ("company", context.CompanyId.ToString()),
                ("correlation", context.CorrelationId), ("request", context.RequestId), ("trace", Activity.Current?.TraceId.ToString() ?? ""));
            await using var uow = new PostgresTransaction(context, connection, transaction);
            var committing = false;
            try
            {
                var result = await work(uow, cancellationToken);
                await uow.FlushAsync(cancellationToken);
                committing = true;
                await transaction.CommitAsync(cancellationToken);
                FoundationTelemetry.Transactions.Add(1, new KeyValuePair<string, object?>("outcome", "committed"));
                return result;
            }
            catch (Exception e) when (Retryable(e) && attempt < 2)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                FoundationTelemetry.Retries.Add(1);
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1) + Random.Shared.Next(10)), cancellationToken);
            }
            catch (Exception e) when (committing && e is not PostgresException && e is NpgsqlException or OperationCanceledException or IOException)
            {
                // The server may have committed. Never blindly re-run a new intent.
                throw new CommitOutcomeUnknownException(e);
            }
        }
    }
    private static bool Retryable(Exception error) => error is PostgresException { SqlState: "40001" or "40P01" }
        || error is DbUpdateException { InnerException: PostgresException { SqlState: "40001" or "40P01" } };
}

public static class Sql
{
    public static NpgsqlCommand Command(NpgsqlConnection connection, string text, params (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(text, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
    public static async Task<int> ExecuteAsync(NpgsqlConnection c, string text, CancellationToken token = default,
        params (string Name, object Value)[] values)
    {
        await using var command = Command(c, text, values); return await command.ExecuteNonQueryAsync(token);
    }
}
