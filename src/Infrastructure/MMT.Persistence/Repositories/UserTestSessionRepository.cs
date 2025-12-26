using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class UserTestSessionRepository(ApplicationDbContext context) : IUserTestSessionRepository
{
    public async Task<UserTestSession?> GetActiveByUserAsync(long chatId, CancellationToken ct = default)
    {
        return await context.TestSessions
            .Include(s => s.Subject)
            .FirstOrDefaultAsync(s => s.ChatId == chatId && s.IsActive, ct);
    }
    
    public async Task<UserTestSession?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.TestSessions
            .Include(s => s.Subject)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }    
    public async Task AddAsync(UserTestSession session, CancellationToken ct = default)
    {
        await context.TestSessions.AddAsync(session, ct); 
    }
    
    public void Update(UserTestSession session)
    {
        context.TestSessions.Update(session); 
    }
    
    public async Task<int> GetUserQuestionCountAsync(long chatId, CancellationToken ct = default)
    {
        var session = await GetActiveByUserAsync(chatId, ct);
        return session?.CurrentQuestionNumber ?? 0;
    }
}
