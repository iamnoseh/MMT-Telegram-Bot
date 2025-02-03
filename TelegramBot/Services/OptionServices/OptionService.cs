using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.OptionServices;

public class OptionService : IOptionService
{
    private readonly DataContext context;
    public Task<bool> AddOptionsAsync(Option option)
    {
        throw new NotImplementedException();
    }

    public async Task<GetOptionDTO> GetOptionAsync(int requestId)
{
    try
    {
        var option = await context.Options.FirstOrDefaultAsync(x => x.OptionId == requestId);
        if (option != null)
        {
            return new GetOptionDTO
            {
                FirstVariant = option.FirstVariant,
                SecondVariant = option.SecondVariant,
                ThirdVariant = option.ThirdVariant,
                FourthVariant = option.FourthVariant
            };
        }
        else
        {
            throw new ArgumentException("Option not found");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in GetOptionAsync: {ex.Message}");
        throw; // Перебрасываем исключение для дальнейшей обработки
    }
}


    public async Task<List<GetOptionDTO>> GetOptionsAsyncs()
    {
        var options =await context.Options.Select(x=> new GetOptionDTO
        {
            FirstVariant = x.FirstVariant,
            SecondVariant = x.SecondVariant,
            ThirdVariant = x.ThirdVariant,
            FourthVariant = x.FourthVariant,
            Answer = x.Answer,
        }).ToListAsync();
        return new List<GetOptionDTO>(options);
    }

    public Task<bool> RemoveOptionsAsync(int requestId)
    {
        throw new NotImplementedException();
    }

    public Task<bool> UpdateOptionsAsync(Option option)
    {
        throw new NotImplementedException();
    }

}
