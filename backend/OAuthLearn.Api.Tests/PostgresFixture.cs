using Microsoft.EntityFrameworkCore;
using Npgsql;
using OAuthLearn.Api.Data;
using Testcontainers.PostgreSql;

namespace OAuthLearn.Api.Tests;

/// <summary>
/// Provides a throwaway, migrated PostgreSQL database for the test run.
/// Uses TEST_PG_CONNECTION when set (the role needs CREATEDB), otherwise starts a Testcontainers
/// postgres:16 container (needs Docker).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ConnectionEnvVar = "TEST_PG_CONNECTION";

    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ConnectionEnvVar);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            _container = new PostgreSqlBuilder("postgres:16").Build();
            try
            {
                await _container.StartAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Database tests need Docker running, or {ConnectionEnvVar} set to a Postgres connection " +
                    "string whose role has CREATEDB (see quickstart.md).", ex);
            }

            baseConnection = _container.GetConnectionString();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = $"oauthlearn_test_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString != "")
        {
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
