using MMT.Domain.Entities;

namespace MMT.Application.Common.Interfaces.Repositories;

public interface IInvitationRepository
{
    Task<Invitation?> GetPendingAsync(long inviteeChatId, CancellationToken ct = default);
    Task<List<Invitation>> GetByInviterAsync(long inviterChatId, CancellationToken ct = default);
    Task AddAsync(Invitation invitation, CancellationToken ct = default);
    void Update(Invitation invitation);
}
