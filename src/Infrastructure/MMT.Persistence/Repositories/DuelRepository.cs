using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;
namespace MMT.Persistence.Repositories;

public class DuelRepository(ApplicationDbContext context) : IDuelRepository
{
    public async Task<Duel?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.Duels.FindAsync([id], ct);
    }

    public async Task<Duel?> GetByIdWithDetailsAsync(int id, CancellationToken ct = default)
    {
        return await context.Duels
            .Include(d => d.Challenger)
            .Include(d => d.Opponent)
            .Include(d => d.Subject)
            .Include(d => d.Answers)
                .ThenInclude(a => a.Question)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
    }
    
    public async Task<Duel?> GetByCodeAsync(string duelCode, CancellationToken ct = default)
    {
        return await context.Duels
            .Include(d => d.Challenger)
            .Include(d => d.Opponent)
            .Include(d => d.Subject)
            .Include(d => d.Answers)
            .FirstOrDefaultAsync(d => d.DuelCode == duelCode, ct);
    }

    public async Task<List<Duel>> GetActiveDuelsForUserAsync(int userId, CancellationToken ct = default)
    {
        return await context.Duels
            .Include(d => d.Challenger)
            .Include(d => d.Opponent)
            .Include(d => d.Subject)
            .Where(d => (d.ChallengerId == userId || d.OpponentId == userId) 
                     && d.Status == DuelStatus.Active)
            .ToListAsync(ct);
    }

    public async Task<List<Duel>> GetPendingDuelsForUserAsync(int userId, CancellationToken ct = default)
    {
        return await context.Duels
            .Include(d => d.Challenger)
            .Include(d => d.Subject)
            .Where(d => d.OpponentId == userId && d.Status == DuelStatus.Pending)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Duel duel, CancellationToken ct = default)
    {
        await context.Duels.AddAsync(duel, ct);
    }

    public void Update(Duel duel)
    {
        context.Duels.Update(duel);
    }
}
