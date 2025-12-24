using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IRegistrationSessionRepository
{
    Task<RegistrationSession?> GetActiveByChatIdAsync(long chatId, CancellationToken ct = default);
    Task AddAsync(RegistrationSession session, CancellationToken ct = default);
    void Update(RegistrationSession session);
    void Delete(RegistrationSession session);
}
