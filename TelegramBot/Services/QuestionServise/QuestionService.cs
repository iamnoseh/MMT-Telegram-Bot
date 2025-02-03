using Domain.DTOs;
using Microsoft.EntityFrameworkCore;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;
using TelegramBot.Services.QuestionServise;

public class QuestionService(DataContext _context) : IQuestionService
{
    public async Task<bool> AddQuestionsAsync(Question request)
    {
        try
        {
            await _context.Questions.AddAsync(request);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми илова кардани савол: {ex.Message}");
            return false;
        }
    }

    public async Task<GetOptionDTO?> GetOptionDTOAsync(int questionId)
    {
        var options = await _context.Questions
                                    .Where(x => x.QuestionId == questionId)
                                    .Select(x => x.Option)
                                    .FirstOrDefaultAsync();

        if (options == null) return null;

        return new GetOptionDTO
        {
            FirstVariant = options.FirstVariant,
            SecondVariant = options.SecondVariant,
            ThirdVariant = options.ThirdVariant,
            FourthVariant = options.FourthVariant,
            Answer = options.Answer
        };
    }

    public async Task<List<GetOptionDTO>> GetOptionsAsync()
    {
        var options = await _context.Questions
                                    .Select(x => x.Option)
                                    .Select(e => new GetOptionDTO
                                    {
                                        FirstVariant = e.FirstVariant,
                                        SecondVariant = e.SecondVariant,
                                        ThirdVariant = e.ThirdVariant,
                                        FourthVariant = e.FourthVariant,
                                        Answer = e.Answer
                                    }).ToListAsync();
        return options;
    }

    public async Task<GetQuestionWithOptionsDTO?> GetQuestionWithOptionsDTO()
    {
        var questions = await _context.Questions
            .Include(q => q.Option)
            .ToListAsync();

        if (!questions.Any())
            return null;

        var random = new Random();
        var question = questions[random.Next(questions.Count)];

        return new GetQuestionWithOptionsDTO
        {
            QuestionId = question.QuestionId,
            QuestionText = question.QuestionText,
            FirstOption = question.Option.FirstVariant,
            SecondOption = question.Option.SecondVariant,
            ThirdOption = question.Option.ThirdVariant,
            FourthOption = question.Option.FourthVariant,
            Answer = question.Option.Answer
        };
    }

    public async Task<GetQuestionDTO> GetQuestionAsync(int requestId)
    {
        var question = await _context.Questions.FirstOrDefaultAsync(x => x.QuestionId == requestId);

        if (question == null)
        {
            return new GetQuestionDTO
            {
                QuestionId = 0,
                QuestionText = "Савол ёфтa нашуд"
            };
        }

        return new GetQuestionDTO
        {
            QuestionId = question.QuestionId,
            QuestionText = question.QuestionText
        };
    }

    public async Task<GetQuestionWithOptionsDTO?> GetQuestionById(int questionId)
    {
        var question = await _context.Questions
            .Include(q => q.Option)
            .FirstOrDefaultAsync(q => q.QuestionId == questionId);

        if (question == null)
        {
            Console.WriteLine($"Савол бо ID={questionId} ёфтa нашуд.");
            return null;
        }

        return new GetQuestionWithOptionsDTO
        {
            QuestionId = question.QuestionId,
            QuestionText = question.QuestionText,
            FirstOption = question.Option.FirstVariant,
            SecondOption = question.Option.SecondVariant,
            ThirdOption = question.Option.ThirdVariant,
            FourthOption = question.Option.FourthVariant,
            Answer = question.Option.Answer
        };
    }

    public Task<List<GetOptionDTO>> GetOptionsAsyncs()
    {
        throw new NotImplementedException();
    }

}
