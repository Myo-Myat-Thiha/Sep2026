using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sep2026.Infrastructure.Persistence.Stores;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class PasswordMigrationTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [SqlServerFact]
    public async Task MigrationPreservesLegacyDataAndDoesNotResetPasswordsWhenRepeated()
    {
        await using var context = fixture.CreateContext();
        // This fixture has its own isolated database. Recreate the pre-password schema.
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.Users DROP COLUMN PasswordHash;");
        var id = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        const string name = "Legacy owner";
        const string email = "legacy@example.com";
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Users (UserId, Name, Email) VALUES ({id}, {name}, {email});");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.Portfolios (PortfolioId, OwnerId) VALUES ({portfolioId}, {id});");
        var databaseName = context.Database.GetDbConnection().Database;
        var script = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "002_AddLocalPasswords.sql")))
            .Replace("Gemino", databaseName, StringComparison.Ordinal);

        async Task ApplyAsync()
        {
            foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await context.Database.ExecuteSqlRawAsync(batch);
        }

        await ApplyAsync();
        var saved = await context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(id, saved.UserId);
        Assert.Equal(name, saved.Name);
        Assert.Equal(email, saved.Email);
        Assert.Null(saved.PasswordHash);
        Assert.True(await new SqlServerUserStore(context).SetInitialPasswordAsync(email, "Correct horse battery staple!"));
        var hash = await context.Users.Select(user => user.PasswordHash).SingleAsync();
        await ApplyAsync();
        Assert.Equal(hash, await context.Users.Select(user => user.PasswordHash).SingleAsync());
        Assert.Equal(1, await context.Users.CountAsync());
        Assert.True(await context.Portfolios.AnyAsync(portfolio => portfolio.PortfolioId == portfolioId && portfolio.OwnerId == id));
    }
}
