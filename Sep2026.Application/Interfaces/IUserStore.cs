namespace Sep2026.Application.Interfaces;
public interface IUserStore
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task CreateAsync(Guid userId, string name, string email, CancellationToken cancellationToken = default);
}
