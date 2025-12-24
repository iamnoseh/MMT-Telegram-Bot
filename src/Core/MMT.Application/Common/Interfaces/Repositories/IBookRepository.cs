using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IBookRepository
{
    Task<Book?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<List<Book>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<Book>> GetByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default);
    Task AddAsync(Book book, CancellationToken cancellationToken = default);
    void Update(Book book);
    void Delete(Book book);
}
