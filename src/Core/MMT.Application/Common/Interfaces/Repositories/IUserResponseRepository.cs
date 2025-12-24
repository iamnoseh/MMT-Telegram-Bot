using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IUserResponseRepository
{
    Task<List<UserResponse>> GetByChatIdAsync(long chatId, CancellationToken ct = default);
    Task AddAsync(UserResponse userResponse, CancellationToken ct = default);
}
