using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sep2026.Infrastructure.Persistence;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerFixture.ConnectionVariable)))
            Skip = $"Set {SqlServerFixture.ConnectionVariable} to run isolated SQL Server integration tests.";
    }
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string ConnectionVariable = "GEMINO_TEST_SQLSERVER";
    private readonly string databaseName = $"GeminoRepositoryTests_{Guid.NewGuid():N}";
    private string? adminConnectionString;
    private bool ownsDatabase;
    private string databaseConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var configuredConnection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(configuredConnection)) return;

        var builder = new SqlConnectionStringBuilder(configuredConnection)
        {
            InitialCatalog = "master", Pooling = false
        };
        adminConnectionString = builder.ConnectionString;
        builder.InitialCatalog = databaseName;
        databaseConnectionString = builder.ConnectionString;

        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using (var collisionCheck = connection.CreateCommand())
        {
            collisionCheck.CommandText = "SELECT DB_ID(@name);";
            collisionCheck.Parameters.AddWithValue("@name", databaseName);
            if (await collisionCheck.ExecuteScalarAsync() is not DBNull)
                throw new InvalidOperationException("The generated test database name is already in use.");
        }

        // Only this generated database is ever created or removed, never Gemino.
        ownsDatabase = true;
        try
        {
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                "Sql", "001_CreatePortfolioDatabase.sql"));
            script = script.Replace("Gemino", databaseName, StringComparison.Ordinal);
            foreach (var batch in Regex.Split(script, @"^GO\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 60;
                await command.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public GeminoDbContext CreateContext(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<GeminoDbContext>()
            .UseSqlServer(databaseConnectionString).AddInterceptors(interceptors).Options);

    public async Task DisposeAsync()
    {
        if (!ownsDatabase || adminConnectionString is null) return;
        if (!Regex.IsMatch(databaseName, @"\AGeminoRepositoryTests_[a-f0-9]{32}\z"))
            throw new InvalidOperationException("Refusing to remove an unexpected database name.");

        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
        ownsDatabase = false;
    }
}
