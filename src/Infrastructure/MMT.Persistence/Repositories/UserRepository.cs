using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class UserRepository(ApplicationDbContext context) : IUserRepository
{
    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }
    
    public async Task<User?> GetByChatIdAsync(long chatId, CancellationToken ct = default)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, ct);
    }
    
    public async Task<List<User>> GetTopByScoreAsync(int count, CancellationToken ct = default)
    {
        return await context.Users
            .OrderByDescending(u => u.Score)
            .Take(count)
            .ToListAsync(ct);
    }
    
    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.Users.ToListAsync(ct);
    }
    
    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await context.Users.AddAsync(user, ct);
    }
    
    public void Update(User user)
    {
        context.Users.Update(user);
    }
    
    public void Delete(User user)
    {
        context.Users.Remove(user);
    }
    
    public async Task<bool> ExistsByChatIdAsync(long chatId, CancellationToken ct = default)
    {
        return await context.Users.AnyAsync(u => u.ChatId == chatId, ct);
    }
    
    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        return await context.Users.CountAsync(ct);
    }
}
