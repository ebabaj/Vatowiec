using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

// Test-only transaction boundary. All contexts/requests use one real SQL transaction;
// endpoint commits are suppressed and never own/dispose the outer transaction.
// No seed/reset, no deletes, no trigger disabling; Dispose always rolls back.
internal sealed class PreservedSystemTransaction : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly SqlTransaction transaction;
    private bool disposed;
    public DbContextOptions<SystemDbContext> Options { get; }
    public DbContextOptions<CompanyDbContext> CompanyOptions { get; }
    private PreservedSystemTransaction(SqlConnection connection, SqlTransaction transaction)
    {
        this.connection = connection; this.transaction = transaction;
        Options = new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(connection)
            .AddInterceptors(new BindCommands(transaction), new KeepTransaction(transaction))
            .ReplaceService<IRelationalTransactionFactory, NonOwningTransactions>().Options;
        CompanyOptions = new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(connection)
            .AddInterceptors(new BindCommands(transaction), new KeepTransaction(transaction))
            .ReplaceService<IRelationalTransactionFactory, NonOwningTransactions>().Options;
    }
    public static async Task<PreservedSystemTransaction> Open(string database = "Vatowiec_System")
    {
        if (database is not ("Vatowiec_System" or "Vatowiec_Company_A_Dev" or "Vatowiec_Company_B_Dev")) throw new InvalidOperationException("Unknown test database");
        var connection = new SqlConnection($"Server=localhost\\SQLEXPRESS;Database={database};Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))=@machine
                AND CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName'))=N'SQLEXPRESS'
                AND CONVERT(int,SERVERPROPERTY('ProductMajorVersion'))=17
                AND CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE N'Express%'
                AND CONVERT(nvarchar(128),CONNECTIONPROPERTY('auth_scheme')) IN (N'NTLM',N'KERBEROS')
                AND EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.LocalTestPurpose'
                AND CONVERT(nvarchar(256),value)=N'VATowiec M0 fictional company demonstration v1') THEN 1 ELSE 0 END
                """;
            command.Parameters.AddWithValue("@machine", Environment.MachineName);
            Assert.Equal(1, await command.ExecuteScalarAsync());
            return new(connection, (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted));
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public SystemDbContext Context() => new(Options);
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { await transaction.RollbackAsync(); }
        finally { await transaction.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class BindCommands(SqlTransaction transaction) : DbCommandInterceptor
    {
        public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
        { result.Transaction = transaction; return result; }
    }
    private sealed class KeepTransaction(SqlTransaction transaction) : DbTransactionInterceptor
    {
        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
            => InterceptionResult<DbTransaction>.SuppressWithResult(transaction);
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<DbTransaction>.SuppressWithResult(transaction));
        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result) => InterceptionResult.Suppress();
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }
    public sealed class NonOwningTransactions(RelationalTransactionFactoryDependencies dependencies) : IRelationalTransactionFactory
    {
        public RelationalTransaction Create(IRelationalConnection connection, DbTransaction transaction, Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned) =>
            new SavepointTransaction(connection, transaction, transactionId, logger, dependencies.SqlGenerationHelper);
    }
    // Reproduce endpoint commit/rollback semantics inside the preserved outer transaction.
    private sealed class SavepointTransaction : RelationalTransaction
    {
        private readonly DbTransaction root;
        private readonly string savepoint = "test_" + Guid.NewGuid().ToString("N")[..24];
        private bool completed;
        public SavepointTransaction(IRelationalConnection connection, DbTransaction transaction, Guid id,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper sql)
            : base(connection, transaction, id, logger, transactionOwned: false, sql)
        { root = transaction; root.Save(savepoint); }
        public override void Commit() => completed = true;
        public override Task CommitAsync(CancellationToken ct = default) { completed = true; return Task.CompletedTask; }
        public override void Rollback() { root.Rollback(savepoint); completed = true; }
        public override async Task RollbackAsync(CancellationToken ct = default) { await root.RollbackAsync(savepoint, ct); completed = true; }
        public override void Dispose() { if (!completed) Rollback(); base.Dispose(); }
        public override async ValueTask DisposeAsync() { if (!completed) await RollbackAsync(); await base.DisposeAsync(); }
    }
}
