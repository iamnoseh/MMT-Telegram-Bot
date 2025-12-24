using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace MMT.Persistence.Repositories;

public class UserResponseRepository(ApplicationDbContext context) : IUserResponseRepository
{
    public async Task<List<UserResponse>> GetByChatIdAsync(long chatId, CancellationToken ct = default)
    {
        return await context.UserResponses
            .Where(ur => ur.ChatId == chatId)
            .OrderByDescending(ur => ur.CreatedAt)
            .ToListAsync(ct);
    }
    
    public async Task AddAsync(UserResponse userResponse, CancellationToken ct = default)
    {
        await context.UserResponses.AddAsync(userResponse, ct);
    }
}
