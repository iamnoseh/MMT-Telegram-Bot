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
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }
    
    public async Task<List<Book>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.Books
            .Include(b => b.UploadedByUser)
            .ToListAsync(ct);
    }
    
    public async Task<List<Book>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await context.Books
            .Where(b => b.IsActive)
            .Include(b => b.UploadedByUser)
            .ToListAsync(ct);
    }
    
    public async Task<List<Book>> GetByCategoryIdAsync(int categoryId, CancellationToken ct = default)
    {
             return await context.Books
            .Where(b => b.IsActive)
            .ToListAsync(ct);
    }
    
    public async Task<List<Book>> GetByCategoryAsync(int categoryId, CancellationToken ct = default)
    {
        return await context.Books
            .Where(b => b.IsActive)
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
