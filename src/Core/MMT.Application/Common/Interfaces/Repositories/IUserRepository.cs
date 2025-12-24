using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<User?> GetByChatIdAsync(long chatId, CancellationToken ct = default);
    Task<List<User>> GetTopByScoreAsync(int count, CancellationToken ct = default);
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
    void Delete(User user);
    Task<bool> ExistsByChatIdAsync(long chatId, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
}
