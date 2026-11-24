namespace Sep2026.Application.Exceptions;

public sealed class InvestmentAccountReadException : Exception
{
    // Only sanitized messages created by the adapter cross this boundary.
    public InvestmentAccountReadException(string message) : base(message) { }
}
