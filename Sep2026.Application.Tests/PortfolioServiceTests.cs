using Sep2026.Application.DTOs;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Services;
using Sep2026.Domain;
using Xunit;

namespace Sep2026.Application.Tests;

public class PortfolioServiceTests
{
    private static readonly DateTimeOffset SavedAt =
        new(2024, 2, 3, 10, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public async Task ReturnsPortfolioDetailsWithHoldingAndQuoteData()
    {
        var portfolio = CreatePortfolio();
        var repository = new StubPortfolioRepository { Result = portfolio };
        var service = CreateService(repository);

        var result = await service.GetByIdAsync(portfolio.PortfolioId);

        Assert.NotNull(result);
        Assert.Equal(portfolio.PortfolioId, result.PortfolioId);
        Assert.Equal(portfolio.OwnerId, result.OwnerId);
        Assert.Equal(1200.12345678m, result.CashBalance);
        Assert.Equal("Retirement", result.PortfolioType);
        Assert.Equal("GBP", result.CurrencyType);
        Assert.Equal("Archived", result.Status);
        Assert.Equal(SavedAt, result.CreatedAt);
        Assert.Equal(SavedAt.Offset, result.CreatedAt.Offset);
        Assert.Equal(2, result.Holdings.Count);

        var sourceHolding = portfolio.Holdings[0];
        var holding = result.Holdings[0];
        Assert.Equal(sourceHolding.HoldingId, holding.HoldingId);
        Assert.Equal(portfolio.PortfolioId, holding.PortfolioId);
        Assert.Equal(sourceHolding.SecurityId, holding.SecurityId);
        Assert.Equal("AAPL", holding.TickerSymbol);
        Assert.Equal("Stock", holding.SecurityType);
        Assert.Equal(10, holding.ShareCount);
        Assert.Equal(25.12345678m, holding.AveragePurchasePrice);
        var quote = Assert.IsType<MarketQuoteDto>(holding.LatestQuote);
        Assert.Equal("AAPL", quote.TickerSymbol);
        Assert.Equal(42.125m, quote.Price);
        Assert.Equal("USD", quote.CurrencyType);
        Assert.Equal(SavedAt.AddDays(1), quote.QuoteAt);

        Assert.Equal(0, result.Holdings[1].ShareCount);
        Assert.Equal(0m, result.Holdings[1].AveragePurchasePrice);
        Assert.Null(result.Holdings[1].LatestQuote);
    }

    [Fact]
    public async Task PassesTheRequestedIdAndCancellationTokenToTheRepository()
    {
        var repository = new StubPortfolioRepository();
        var portfolioId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        await CreateService(repository).GetByIdAsync(portfolioId, cancellation.Token);
        Assert.Equal(1, repository.Calls);
        Assert.Equal(portfolioId, repository.LastId);
        Assert.Equal(cancellation.Token, repository.LastToken);
    }

    [Fact]
    public async Task ReturnsNullWhenTheRepositoryCannotFindThePortfolio()
    {
        var repository = new StubPortfolioRepository();
        Assert.Null(await CreateService(repository).GetByIdAsync(Guid.NewGuid()));
        Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task RejectsAnEmptyIdBeforeCallingTheRepository()
    {
        var repository = new StubPortfolioRepository();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService(repository).GetByIdAsync(Guid.Empty));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task HonorsCancellationBeforeCallingTheRepository()
    {
        var repository = new StubPortfolioRepository();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(repository).GetByIdAsync(Guid.NewGuid(), new CancellationToken(true)));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task DoesNotHideRepositoryFailuresAsMissingData()
    {
        var failure = new InvalidDataException("Invalid saved portfolio state.");
        var repository = new StubPortfolioRepository { Failure = failure };
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(repository).GetByIdAsync(Guid.NewGuid()));
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task ReturnsAnEmptyHoldingListForAnEmptyPortfolio()
    {
        var portfolio = CreatePortfolio(withHoldings: false);
        var service = CreateService(new StubPortfolioRepository { Result = portfolio });
        var result = await service.GetByIdAsync(portfolio.PortfolioId);
        Assert.NotNull(result);
        Assert.Empty(result.Holdings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task KeepsKnownQuotePricesDistinctFromMissingQuotes(int price)
    {
        var portfolio = CreatePortfolio();
        portfolio.Holdings[0].Security.UpdateMarketQuote(new MarketQuote("AAPL", price, "USD", SavedAt));
        var result = await CreateService(new StubPortfolioRepository { Result = portfolio })
            .GetByIdAsync(portfolio.PortfolioId);

        Assert.NotNull(result);
        var quote = Assert.IsType<MarketQuoteDto>(result.Holdings[0].LatestQuote);
        Assert.Equal((decimal)price, quote.Price);
        Assert.Null(result.Holdings[1].LatestQuote);
    }

    [Fact]
    public async Task ReturnedDetailsAreIndependentOfLaterDomainChanges()
    {
        var portfolio = CreatePortfolio(active: true);
        var result = await CreateService(new StubPortfolioRepository { Result = portfolio })
            .GetByIdAsync(portfolio.PortfolioId);
        Assert.NotNull(result);

        portfolio.DepositCash(100m);
        portfolio.Holdings[0].Security.UpdateMarketQuote(new MarketQuote("AAPL", 99m, "GBP", SavedAt.AddDays(2)));
        portfolio.Holdings[0].Security.UpdateSecurityType("Updated type");

        Assert.Equal(1200.12345678m, result.CashBalance);
        Assert.Equal("Stock", result.Holdings[0].SecurityType);
        var quote = Assert.IsType<MarketQuoteDto>(result.Holdings[0].LatestQuote);
        Assert.Equal(42.125m, quote.Price);
        Assert.Equal("USD", quote.CurrencyType);
        Assert.Equal(SavedAt.AddDays(1), quote.QuoteAt);
        var holdings = Assert.IsAssignableFrom<IList<HoldingDetailsDto>>(result.Holdings);
        Assert.True(holdings.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => holdings.Clear());
    }

    [Fact]
    public async Task UserPortfolioListUsesSummaryQueriesWithoutLoadingDetailGraphs()
    {
        var userId = Guid.NewGuid();
        IReadOnlyList<PortfolioSummaryDto> summaries = Array.AsReadOnly(new[]
        {
            new PortfolioSummaryDto(Guid.NewGuid(), PortfolioType.Retirement,
                CurrencyType.GBP, 1200m, PortfolioStatus.Archived, SavedAt)
        });
        var repository = new StubPortfolioRepository();
        var queries = new StubPortfolioQueries { Result = summaries };
        using var cancellation = new CancellationTokenSource();

        var result = await CreateService(repository, queries).GetByUserIdAsync(userId, cancellation.Token);

        Assert.Same(summaries, result);
        Assert.Equal(1, queries.Calls);
        Assert.Equal(userId, queries.LastId);
        Assert.Equal(cancellation.Token, queries.LastToken);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task UserPortfolioListReturnsAnEmptyCollectionWhenNothingMatches()
    {
        var result = await CreateService(new StubPortfolioRepository()).GetByUserIdAsync(Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public async Task UserPortfolioListRejectsEmptyIdsBeforeCallingQueries()
    {
        var queries = new StubPortfolioQueries();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService(new StubPortfolioRepository(), queries).GetByUserIdAsync(Guid.Empty));
        Assert.Equal(0, queries.Calls);
    }

    [Fact]
    public async Task UserPortfolioListHonorsCancellationBeforeCallingQueries()
    {
        var queries = new StubPortfolioQueries();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(new StubPortfolioRepository(), queries)
                .GetByUserIdAsync(Guid.NewGuid(), new CancellationToken(true)));
        Assert.Equal(0, queries.Calls);
    }

    [Fact]
    public async Task UserPortfolioListDoesNotHideQueryFailuresAsEmptyResults()
    {
        var failure = new InvalidDataException("Unsupported stored currency.");
        var queries = new StubPortfolioQueries { Failure = failure };
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateService(new StubPortfolioRepository(), queries).GetByUserIdAsync(Guid.NewGuid()));
        Assert.Same(failure, exception);
    }

    private static PortfolioService CreateService(
        StubPortfolioRepository repository, StubPortfolioQueries? queries = null)
        => new(repository, queries ?? new StubPortfolioQueries());

    private static PortfolioModel CreatePortfolio(bool withHoldings = true, bool active = false)
    {
        var portfolioId = Guid.NewGuid();
        var holdings = new List<HoldingModel>();
        if (withHoldings)
        {
            var quoted = SecurityModel.Restore(Guid.NewGuid(), "AAPL", "Stock",
                new MarketQuote("AAPL", 42.125m, "USD", SavedAt.AddDays(1)));
            var unquoted = new SecurityModel("MSFT");
            holdings.Add(HoldingModel.Restore(Guid.NewGuid(), quoted, portfolioId, 10, 25.12345678m));
            holdings.Add(HoldingModel.Restore(Guid.NewGuid(), unquoted, portfolioId, 0, 0m));
        }
        return PortfolioModel.Restore(portfolioId, 1200.12345678m, holdings, Guid.NewGuid(),
            PortfolioType.Retirement, CurrencyType.GBP, SavedAt,
            active ? PortfolioStatus.Active : PortfolioStatus.Archived);
    }

    private sealed class StubPortfolioQueries : IPortfolioQueries
    {
        public IReadOnlyList<PortfolioSummaryDto> Result { get; init; } = Array.Empty<PortfolioSummaryDto>();
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Guid LastId { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastId = userId;
            LastToken = cancellationToken;
            return Failure is null
                ? Task.FromResult(Result)
                : Task.FromException<IReadOnlyList<PortfolioSummaryDto>>(Failure);
        }
    }

    private sealed class StubPortfolioRepository : IPortfolioRepository
    {
        public PortfolioModel? Result { get; init; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Guid LastId { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<PortfolioModel?> GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastId = portfolioId;
            LastToken = cancellationToken;
            return Failure is null
                ? Task.FromResult(Result)
                : Task.FromException<PortfolioModel?>(Failure);
        }
    }
}
