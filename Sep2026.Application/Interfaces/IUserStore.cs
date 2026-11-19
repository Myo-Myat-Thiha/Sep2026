using Sep2026.Application.DTOs;

namespace Sep2026.Application.Interfaces;
public interface IUserStore
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task CreateAsync(Guid userId, string name, string email, string password, CancellationToken cancellationToken = default);
    Task<UserDto?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);
    // Trusted-device bootstrap only: atomically sets a password if none exists.
    Task<bool> SetInitialPasswordAsync(string email, string password, CancellationToken cancellationToken = default);
}
