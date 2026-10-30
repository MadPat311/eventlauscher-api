using EventLauscherApi.Data;
using EventLauscherApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EventLauscherApi.Tests.Infrastructure;

internal sealed class PostgreSqlDatabase : IAsyncDisposable
{
    public const string ConnectionVariable = "EVENTLAUSCHER_TEST_POSTGRES";
    private readonly string _adminConnection;
    private readonly string _name = "eventlauscher_test_" + Guid.NewGuid().ToString("N");
    private bool _created;
    public string ConnectionString { get; }

    private PostgreSqlDatabase(string adminConnection)
    {
        _adminConnection = adminConnection;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = _name, Pooling = false
        }.ConnectionString;
    }

    public static async Task<PostgreSqlDatabase> CreateAsync()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ConnectionVariable} to a dedicated PostgreSQL test instance.");
        var database = new PostgreSqlDatabase(connection);
        try
        {
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database._name}\"", admin);
            await create.ExecuteNonQueryAsync();
            database._created = true;
            await using var db = database.Context();
            // The historical migrations require an existing baseline. These tests use
            // the current EF model; migration execution has a separate focused test.
            await db.Database.EnsureCreatedAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public EventContext Context() => new(new DbContextOptionsBuilder<EventContext>().UseNpgsql(ConnectionString).Options);

    public async Task<AppUser> AddUser(string username = "tester")
    {
        await using var db = Context();
        var user = new AppUser
        {
            Id = Guid.NewGuid(), UserName = username, NormalizedUserName = username.ToUpperInvariant(),
            Email = $"{username}@example.test", NormalizedEmail = $"{username}@example.test".ToUpperInvariant()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_created) return;
        // Only the generated database is ever dropped, never the configured admin DB.
        if (!_name.StartsWith("eventlauscher_test_", StringComparison.Ordinal)
            || !Guid.TryParseExact(_name["eventlauscher_test_".Length..], "N", out _))
            throw new InvalidOperationException("Unexpected test database name.");
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{_name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlDatabase.ConnectionVariable)))
            Skip = "Requires a dedicated PostgreSQL test instance; see Tests/README.md.";
    }
}

public sealed class PostgreSqlTheoryAttribute : TheoryAttribute
{
    public PostgreSqlTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlDatabase.ConnectionVariable)))
            Skip = "Requires a dedicated PostgreSQL test instance; see Tests/README.md.";
    }
}
