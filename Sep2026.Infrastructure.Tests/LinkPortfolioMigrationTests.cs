using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class LinkPortfolioMigrationTests
{
    [SqlServerFact]
    public async Task MigrationPreservesLegacyRowsSupportsNewValuesAndCanBeRepeated()
    {
        var fixture = new SqlServerFixture { IncludeLinkSchema = false };
        await fixture.InitializeAsync();
        try
        {
            await using (var context = fixture.CreateContext())
                await context.Database.ExecuteSqlRawAsync("""
                    DECLARE @owner uniqueidentifier=NEWID(), @portfolio uniqueidentifier=NEWID(), @security uniqueidentifier=NEWID();
                    INSERT dbo.Users(UserId,Name,Email) VALUES (@owner,N'Legacy',N'legacy@example.com');
                    INSERT dbo.Portfolios(PortfolioId,OwnerId,CashBalance) VALUES (@portfolio,@owner,100);
                    INSERT dbo.Securities(SecurityId,TickerSymbol,SecurityType) VALUES (@security,N'AAPL',N'Stock');
                    INSERT dbo.Holdings(PortfolioId,SecurityId,ShareCount,AveragePurchasePrice) VALUES (@portfolio,@security,2,25);
                    """);
            await fixture.ApplyScriptAsync("003_LinkPortfolio.sql");
            await fixture.ApplyScriptAsync("003_LinkPortfolio.sql");
            await using var updated = fixture.CreateContext();
            var portfolio = await updated.Portfolios.Include(item => item.Holdings).SingleAsync();
            Assert.Equal(100m, portfolio.CashBalance);
            Assert.Equal(2m, Assert.Single(portfolio.Holdings).ShareCount);
            Assert.Equal(25m, portfolio.Holdings.Single().AveragePurchasePrice);
            Assert.Null(portfolio.DisplayName);
            Assert.Empty(await updated.ProviderConnections.ToListAsync());
            portfolio.Holdings.Single().ShareCount = 1.123456789m;
            portfolio.Holdings.Single().AveragePurchasePrice = null;
            await updated.SaveChangesAsync();
            await using var reloaded = fixture.CreateContext();
            Assert.Equal(1.123456789m, (await reloaded.Holdings.SingleAsync()).ShareCount);
            Assert.Null((await reloaded.Holdings.SingleAsync()).AveragePurchasePrice);
        }
        finally { await fixture.DisposeAsync(); }
    }
}
