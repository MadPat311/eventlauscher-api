using EventLauscherApi.Data;
using EventLauscherApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace EventLauscherApi.Tests.Data;

public sealed class SeedTests
{
    [Fact]
    public async Task SeedRequestsMigrationsAndCreatesRolesAndConfirmedAdminIdempotently()
    {
        using var env = new TestEnvironment();
        // Exercise seeding with real Identity stores. Replace only the migration executor
        // so no PostgreSQL connection is opened by this offline test.
        await using var migrationDb = new EventContext(new DbContextOptionsBuilder<EventContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused")
            .ReplaceService<IMigrator, RecordingMigrator>().Options);
        var services = new ServiceCollection();
        services.AddSingleton(env.Users);
        services.AddSingleton(env.Roles);
        services.AddSingleton(migrationDb);
        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        await Seed.EnsureSeedAsync(app);
        var admin = (await env.Users.FindByEmailAsync("admin@eventlauscher.de"))!;
        Assert.Equal("admin", admin.UserName);
        Assert.True(admin.EmailConfirmed);
        Assert.True(await env.Users.CheckPasswordAsync(admin, "Admin!123"));
        Assert.Equal(new[] { "Admin", "Reviewer", "User" }, (await env.Users.GetRolesAsync(admin)).OrderBy(x => x));
        await Seed.EnsureSeedAsync(app);
        Assert.Equal(3, env.Roles.Roles.Count());
        Assert.Equal(2, env.Users.Users.Count());
        Assert.Equal(2, Assert.IsType<RecordingMigrator>(migrationDb.GetService<IMigrator>()).Calls);
    }

    public sealed class RecordingMigrator : IMigrator
    {
        public int Calls { get; private set; }
        public void Migrate(string? targetMigration = null) => Calls++;
        public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.CompletedTask;
        }
        public string GenerateScript(string? fromMigration = null, string? toMigration = null, MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
            => throw new NotSupportedException();
    }
}
