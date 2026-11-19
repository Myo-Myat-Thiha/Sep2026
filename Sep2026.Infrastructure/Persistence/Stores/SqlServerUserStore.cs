using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Sep2026.Application.Exceptions;
using Sep2026.Application.DTOs;
using Sep2026.Application.Interfaces;
using Sep2026.Domain;
using DbUser = Sep2026.Infrastructure.Persistence.Entities.User;

namespace Sep2026.Infrastructure.Persistence.Stores;

public sealed class SqlServerUserStore : IUserStore
{
    private readonly GeminoDbContext context;
    private readonly PasswordHasher<DbUser> hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));

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

    public async Task CreateAsync(Guid userId, string name, string email, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty)
            throw new ArgumentException("A user ID is required.", nameof(userId));
        UserProfileRules.ValidateName(name);
        UserProfileRules.ValidateEmail(email);
        UserProfileRules.ValidatePassword(password);
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Create a user in a scope without other pending writes.");

        var entity = new DbUser { UserId = userId, Name = name, Email = email };
        entity.PasswordHash = hasher.HashPassword(entity, password);
        cancellationToken.ThrowIfCancellationRequested();
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

    public async Task<UserDto?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();
        if (password.Length == 0 || password.Length > UserProfileRules.MaxPasswordLength) return null;
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (string.IsNullOrEmpty(user?.PasswordHash)) return null;
        PasswordVerificationResult result;
        try { result = hasher.VerifyHashedPassword(user, user.PasswordHash, password); }
        catch (FormatException) { return null; }
        if (result == PasswordVerificationResult.Failed) return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var oldHash = user.PasswordHash;
            var upgraded = hasher.HashPassword(user, password);
            var changed = await context.Users.Where(candidate => candidate.UserId == user.UserId && candidate.PasswordHash == oldHash)
                .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.PasswordHash, upgraded), cancellationToken);
            if (changed != 1) return null;
        }
        return new UserDto(user.UserId, user.Name, user.Email);
    }

    public async Task<bool> SetInitialPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        UserProfileRules.ValidatePassword(password);
        cancellationToken.ThrowIfCancellationRequested();
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email && user.PasswordHash == null, cancellationToken);
        if (user is null) return false;
        var hash = hasher.HashPassword(user, password);
        return await context.Users.Where(candidate => candidate.UserId == user.UserId && candidate.PasswordHash == null)
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.PasswordHash, hash), cancellationToken) == 1;
    }

    private static bool IsDuplicateEmail(DbUpdateException exception)
        => exception.InnerException is SqlException sql && sql.Errors.Cast<SqlError>().Any(error =>
            error.Number is 2601 or 2627 &&
            error.Message.Contains("'UQ_Users_Email'", StringComparison.Ordinal));
}
