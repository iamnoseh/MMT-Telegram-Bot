using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface ISubjectRepository
{
    Task<Subject?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<Subject>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Subject subject, CancellationToken ct = default);
    void Update(Subject subject);
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);
}
