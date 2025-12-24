using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class RegistrationSessionRepository(ApplicationDbContext context) : IRegistrationSessionRepository
{
    public async Task<RegistrationSession?> GetActiveByChatIdAsync(long chatId, CancellationToken ct = default)
    {
        return await context.RegistrationSessions
            .FirstOrDefaultAsync(s => s.ChatId == chatId && !s.IsCompleted, ct);
    }
    
    public async Task AddAsync(RegistrationSession session, CancellationToken ct = default)
    {
        await context.RegistrationSessions.AddAsync(session, ct);
    }
    
    public void Update(RegistrationSession session)
    {
        context.RegistrationSessions.Update(session);
    }
    
    public void Delete(RegistrationSession session)
    {
        context.RegistrationSessions.Remove(session);
    }
}
