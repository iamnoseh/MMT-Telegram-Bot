using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class UserStateRepository(ApplicationDbContext context) : IUserStateRepository
{
    public async Task<UserState?> GetByChatIdAsync(long chatId, CancellationToken ct = default)
    {
        return await context.UserStates
            .Include(s => s.SelectedSubject)
            .FirstOrDefaultAsync(s => s.ChatId == chatId, ct);
    }
    
    public async Task AddAsync(UserState state, CancellationToken ct = default)
    {
        await context.UserStates.AddAsync(state, ct);
    }
    
    public void Update(UserState state)
    {
        context.UserStates.Update(state);
    }
    
    public async Task<UserState> GetOrCreateAsync(long chatId, CancellationToken ct = default)
    {
        var state = await GetByChatIdAsync(chatId, ct);
        
        if (state == null)
        {
            state = new UserState { ChatId = chatId };
            await AddAsync(state, ct);
            await context.SaveChangesAsync(ct);
        }
        
        return state;
    }
}
