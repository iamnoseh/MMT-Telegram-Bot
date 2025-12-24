using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class BookRepository(ApplicationDbContext context) : IBookRepository
{
    public async Task<Book?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.Books
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }
    
    public async Task<List<Book>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await context.Books
            .Include(b => b.Category)
            .Where(b => b.IsActive)
            .ToListAsync(ct);
    }
    
    public async Task<List<Book>> GetByCategoryAsync(int categoryId, CancellationToken ct = default)
    {
        return await context.Books
            .Where(b => b.CategoryId == categoryId && b.IsActive)
            .ToListAsync(ct);
    }
    
    public async Task AddAsync(Book book, CancellationToken ct = default)
    {
        await context.Books.AddAsync(book, ct);
    }
    
    public void Update(Book book)
    {
        context.Books.Update(book);
    }
    
    public void Delete(Book book)
    {
        context.Books.Remove(book);
    }
}
