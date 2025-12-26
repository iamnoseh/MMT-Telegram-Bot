using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IQuestionRepository
{
    Task<Question?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Question?> GetRandomBySubjectAsync(int subjectId, CancellationToken ct = default);
    Task<List<Question>> GetBySubjectAsync(int subjectId, CancellationToken ct = default);
    Task<bool> ExistsAsync(int subjectId, string questionText, CancellationToken ct = default);
    Task AddAsync(Question question, CancellationToken ct = default);
    Task AddRangeAsync(List<Question> questions, CancellationToken ct = default);
    void Update(Question question);
    void Delete(Question question);
    Task<int> GetCountBySubjectAsync(int subjectId, CancellationToken ct = default);
}
