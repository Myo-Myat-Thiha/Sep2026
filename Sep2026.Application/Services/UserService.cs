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
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(email);
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedName = UserProfileRules.NormalizeName(name);
            var normalizedEmail = UserProfileRules.NormalizeEmail(email);
            if (await store.EmailExistsAsync(normalizedEmail, cancellationToken))
                throw new EmailAlreadyExistsException();
            cancellationToken.ThrowIfCancellationRequested();

            var userId = Guid.NewGuid();
            await store.CreateAsync(userId, normalizedName, normalizedEmail, cancellationToken);
            return new UserDto(userId, normalizedName, normalizedEmail);
        }
    }

}
