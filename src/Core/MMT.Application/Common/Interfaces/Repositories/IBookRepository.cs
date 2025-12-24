using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IBookRepository
{
    Task<Book?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<Book>> GetAllActiveAsync(CancellationToken ct = default);
    Task<List<Book>> GetByCategoryAsync(int categoryId, CancellationToken ct = default);
    Task AddAsync(Book book, CancellationToken ct = default);
    void Update(Book book);
    void Delete(Book book);
}
