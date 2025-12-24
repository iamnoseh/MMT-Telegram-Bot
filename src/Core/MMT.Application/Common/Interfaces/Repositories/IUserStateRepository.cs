using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IUserStateRepository
{
    Task<UserState?> GetByChatIdAsync(long chatId, CancellationToken ct = default);
    Task AddAsync(UserState state, CancellationToken ct = default);
    void Update(UserState state);
    Task<UserState> GetOrCreateAsync(long chatId, CancellationToken ct = default);
}
