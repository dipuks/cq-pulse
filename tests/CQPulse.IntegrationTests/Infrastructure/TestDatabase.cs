using System.Text.RegularExpressions;
using CQPulse.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.IntegrationTests.Infrastructure;

/// <summary>
/// A uniquely named, disposable SQL Server database for one test fixture.
/// Every create/drop is guarded so only databases named <c>CQPulse_Tests_&lt;32 hex&gt;</c> are touched;
/// the development database can never be targeted.
/// </summary>
public sealed partial class TestDatabase : IAsyncDisposable
{
    public const string Prefix = "CQPulse_Tests_";

    /// <summary>Server-level connection string; override with the CQPULSE_TEST_SQL environment variable (e.g. in CI).</summary>
    public const string DefaultServerConnectionString =
        @"Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);
    private bool _dropped;

    private TestDatabase(string name, string connectionString)
    {
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }
    public string ConnectionString { get; }

    [GeneratedRegex(@"^CQPulse_Tests_[0-9a-f]{32}$")]
    private static partial Regex TestNamePattern();

    public static bool IsTestDatabaseName(string? name) => name is not null && TestNamePattern().IsMatch(name);

    /// <summary>Creates a new database and applies all EF Core migrations.</summary>
    public static async Task<TestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        var name = Prefix + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder(ServerConnectionString()) { InitialCatalog = name };
        var database = new TestDatabase(name, builder.ConnectionString);
        EnsureSafe(database.Name);

        await DropStaleTestDatabasesAsync(cancellationToken);
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync(cancellationToken);
        return database;
    }

    public CQPulseDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CQPulseDbContext>().UseSqlServer(ConnectionString).Options);

    public static Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default) =>
        QueryMasterAsync(async command =>
        {
            command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
            command.Parameters.AddWithValue("@name", name);
            return (int)(await command.ExecuteScalarAsync(cancellationToken))! == 1;
        }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_dropped) return;
        _dropped = true;
        await DropAsync(Name, CancellationToken.None);
    }

    internal static void EnsureSafe(string name)
    {
        if (!IsTestDatabaseName(name))
            throw new InvalidOperationException($"Refusing to manage database '{name}': only {Prefix}<guid> databases may be created or dropped by tests.");
    }

    private static string ServerConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("CQPULSE_TEST_SQL");
        var builder = new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(configured) ? DefaultServerConnectionString : configured);
        if (!string.IsNullOrEmpty(builder.InitialCatalog))
            throw new InvalidOperationException("CQPULSE_TEST_SQL must be a server-level connection string without a database name.");
        return builder.ConnectionString;
    }

    private static async Task DropAsync(string name, CancellationToken cancellationToken)
    {
        EnsureSafe(name);
        SqlConnection.ClearAllPools();
        await QueryMasterAsync(async command =>
        {
            // Name is validated against a strict pattern above, so bracket-quoting is safe here.
            command.CommandText = $"""
                IF DB_ID(@name) IS NOT NULL
                BEGIN
                    ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{name}];
                END
                """;
            command.Parameters.AddWithValue("@name", name);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        }, cancellationToken);
    }

    /// <summary>Removes test databases left behind by crashed or killed test runs.</summary>
    private static async Task DropStaleTestDatabasesAsync(CancellationToken cancellationToken)
    {
        var stale = await QueryMasterAsync(async command =>
        {
            command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE 'CQPulse[_]Tests[_]%' AND create_date < @cutoff";
            command.Parameters.AddWithValue("@cutoff", DateTime.Now - StaleAfter);
            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
            return names;
        }, cancellationToken);

        foreach (var name in stale.Where(IsTestDatabaseName)) await DropAsync(name, cancellationToken);
    }

    private static async Task<T> QueryMasterAsync<T>(Func<SqlCommand, Task<T>> query, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(ServerConnectionString()) { InitialCatalog = "master", Pooling = false };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        return await query(command);
    }
}
