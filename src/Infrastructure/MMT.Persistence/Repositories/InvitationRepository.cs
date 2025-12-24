using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class InvitationRepository(ApplicationDbContext context) : IInvitationRepository
{
    public async Task<Invitation?> GetPendingAsync(long inviteeChatId, CancellationToken ct = default)
    {
        return await context.Invitations
            .FirstOrDefaultAsync(i => i.InviteeChatId == inviteeChatId && i.Status == "pending", ct);
    }
    
    public async Task<List<Invitation>> GetByInviterAsync(long inviterChatId, CancellationToken ct = default)
    {
        return await context.Invitations
            .Where(i => i.InviterChatId == inviterChatId)
            .ToListAsync(ct);
    }
    
    public async Task AddAsync(Invitation invitation, CancellationToken ct = default)
    {
        await context.Invitations.AddAsync(invitation, ct);
    }
    
    public void Update(Invitation invitation)
    {
        context.Invitations.Update(invitation);
    }
}
