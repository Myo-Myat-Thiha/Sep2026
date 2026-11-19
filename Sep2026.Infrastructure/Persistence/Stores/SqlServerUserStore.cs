using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Domain;
using DbUser = Sep2026.Infrastructure.Persistence.Entities.User;

namespace Sep2026.Infrastructure.Persistence.Stores;

public sealed class SqlServerUserStore : IUserStore
{
    private readonly GeminoDbContext context;

    public SqlServerUserStore(GeminoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        cancellationToken.ThrowIfCancellationRequested();
        // SQL Server's email collation supplies comparison semantics and index usage.
        return context.Users.AnyAsync(user => user.Email == email, cancellationToken);
    }

    public async Task CreateAsync(Guid userId, string name, string email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty)
            throw new ArgumentException("A user ID is required.", nameof(userId));
        UserProfileRules.ValidateName(name);
        UserProfileRules.ValidateEmail(email);
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Create a user in a scope without other pending writes.");

        var entity = new DbUser { UserId = userId, Name = name, Email = email };
        context.Users.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception))
        {
            throw new EmailAlreadyExistsException("A user with this email already exists.", exception);
        }
        finally
        {
            // Own only this insertion. A failed save must not leave it pending for a later save.
            context.Entry(entity).State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateEmail(DbUpdateException exception)
        => exception.InnerException is SqlException sql && sql.Errors.Cast<SqlError>().Any(error =>
            error.Number is 2601 or 2627 &&
            error.Message.Contains("'UQ_Users_Email'", StringComparison.Ordinal));
}
