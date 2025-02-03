using TelegramBot.Domain.Entities;
namespace TelegramBot.Services.UserResponceService;

public interface IResponseService
{
    Task SaveUserResponse(UserResponse response);
}
