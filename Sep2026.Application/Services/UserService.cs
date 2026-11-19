using Sep2026.Application.DTOs;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Exceptions;
using Sep2026.Domain;

namespace Sep2026.Application.Services
{
    public sealed class UserService
    {
        private readonly IUserStore store;

        public UserService(IUserStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            this.store = store;
        }

        public async Task<UserDto> CreateAsync(
            string name,
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(email);
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedName = UserProfileRules.NormalizeName(name);
            var normalizedEmail = UserProfileRules.NormalizeEmail(email);
            UserProfileRules.ValidatePassword(password);
            if (await store.EmailExistsAsync(normalizedEmail, cancellationToken))
                throw new EmailAlreadyExistsException();
            cancellationToken.ThrowIfCancellationRequested();

            var userId = Guid.NewGuid();
            await store.CreateAsync(userId, normalizedName, normalizedEmail, password, cancellationToken);
            return new UserDto(userId, normalizedName, normalizedEmail);
        }

        public Task<UserDto?> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedEmail = UserProfileRules.NormalizeEmail(email);
            ArgumentNullException.ThrowIfNull(password);
            if (password.Length == 0 || password.Length > UserProfileRules.MaxPasswordLength)
                return Task.FromResult<UserDto?>(null);
            return store.AuthenticateAsync(normalizedEmail, password, cancellationToken);
        }

        public Task<bool> SetInitialPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedEmail = UserProfileRules.NormalizeEmail(email);
            UserProfileRules.ValidatePassword(password);
            return store.SetInitialPasswordAsync(normalizedEmail, password, cancellationToken);
        }
    }

}
