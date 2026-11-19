namespace Sep2026.Application.Exceptions;

/// <summary>
/// A user cannot be created because their email already belongs to a saved user.
/// Used for both the service's duplicate check and a recognized duplicate-email
/// failure during persistence. Infrastructure can preserve the cause as InnerException.
/// </summary>
public sealed class EmailAlreadyExistsException : Exception
{
    private const string DefaultMessage = "A user with this email already exists.";

    public EmailAlreadyExistsException()
        : base(DefaultMessage)
    {
    }

    public EmailAlreadyExistsException(string message)
        : base(message)
    {
    }

    public EmailAlreadyExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
