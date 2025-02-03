using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.OptionServices;

public interface IOptionService
{
    Task<GetOptionDTO> GetOptionAsync(int requestId);
    Task<List<GetOptionDTO>> GetOptionsAsyncs();
    Task<bool> AddOptionsAsync(Option option);
    Task<bool> RemoveOptionsAsync(int requestId);
    Task<bool> UpdateOptionsAsync(Option request);
}
