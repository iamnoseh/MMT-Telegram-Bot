using Microsoft.EntityFrameworkCore;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.OptionServices;

public class OptionService : IOptionService
{
    private readonly DataContext _context;

    public OptionService(DataContext context)
    {
        _context = context;
    }

    public async Task<GetOptionDTO> GetOptionByQuestionId(int questionId)
    {
        var option = await _context.Options
            .FirstOrDefaultAsync(o => o.QuestionId == questionId);

        if (option == null)
            return null;

        return new GetOptionDTO
        {
            Id = option.Id,
            QuestionId = option.QuestionId,
            OptionA = option.OptionA,
            OptionB = option.OptionB,
            OptionC = option.OptionC,
            OptionD = option.OptionD
        };
    }

    public async Task<GetOptionDTO> CreateOption(int questionId, Option option)
    {
        // Санҷед, ки оё савол вуҷуд дорад
        var question = await _context.Questions.FindAsync(questionId);
        if (question == null)
            return null;

        option.QuestionId = questionId;
        _context.Options.Add(option);
        await _context.SaveChangesAsync();

        return new GetOptionDTO
        {
            Id = option.Id,
            QuestionId = option.QuestionId,
            OptionA = option.OptionA,
            OptionB = option.OptionB,
            OptionC = option.OptionC,
            OptionD = option.OptionD
        };
    }

    public async Task<GetOptionDTO> UpdateOption(int questionId, Option option)
    {
        var existingOption = await _context.Options
            .FirstOrDefaultAsync(o => o.QuestionId == questionId);

        if (existingOption == null)
            return null;

        existingOption.OptionA = option.OptionA;
        existingOption.OptionB = option.OptionB;
        existingOption.OptionC = option.OptionC;
        existingOption.OptionD = option.OptionD;
        existingOption.CorrectAnswer = option.CorrectAnswer;

        await _context.SaveChangesAsync();

        return new GetOptionDTO
        {
            Id = existingOption.Id,
            QuestionId = existingOption.QuestionId,
            OptionA = existingOption.OptionA,
            OptionB = existingOption.OptionB,
            OptionC = existingOption.OptionC,
            OptionD = existingOption.OptionD
        };
    }

    public async Task<bool> DeleteOption(int questionId)
    {
        var option = await _context.Options
            .FirstOrDefaultAsync(o => o.QuestionId == questionId);

        if (option == null)
            return false;

        _context.Options.Remove(option);
        await _context.SaveChangesAsync();
        return true;
    }
}
