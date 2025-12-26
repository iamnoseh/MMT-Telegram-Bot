using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IDuelRepository
{
    Task<Duel?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Duel?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default);
    Task<Duel?> GetByCodeAsync(string duelCode, CancellationToken ct = default);
    Task<List<Duel>> GetActiveDuelsForUserAsync(int userId, CancellationToken ct = default);
    Task<List<Duel>> GetPendingDuelsForUserAsync(int userId, CancellationToken ct = default);
    Task AddAsync(Duel duel, CancellationToken ct = default);
    void Update(Duel duel);
}
