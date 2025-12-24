using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IUserTestSessionRepository
{
    Task<UserTestSession?> GetActiveByUserAsync(long chatId, CancellationToken ct = default);
    Task<UserTestSession?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(UserTestSession session, CancellationToken ct = default);
    void Update(UserTestSession session);
    Task<int> GetUserQuestionCountAsync(long chatId, CancellationToken ct = default);
}
