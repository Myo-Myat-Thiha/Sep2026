namespace Sep2026.Application.Exceptions;

public sealed class PortfolioLinkNotCommittedException : Exception
{
    public PortfolioLinkNotCommittedException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

public sealed class PortfolioLinkCommitUnknownException : Exception
{
    public Guid AttemptedPortfolioId { get; }

    public PortfolioLinkCommitUnknownException(Guid attemptedPortfolioId, Exception innerException)
        : base("Could not confirm the portfolio link. Check linked portfolios before retrying; saved credentials were retained for reconciliation.", innerException)
        => AttemptedPortfolioId = attemptedPortfolioId;
}
