using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.UserResponceService;

public class ResponseService(DataContext _context): IResponseService
{
    public async Task SaveUserResponse(UserResponse response)
    {
        _context.UserResponses.Add(response);
        await _context.SaveChangesAsync();
    }
}