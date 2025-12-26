using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.DTOs;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Application.Common.Interfaces.Services;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Questions.Commands.ImportQuestions;

public class ImportQuestionsCommandHandler(
    IUnitOfWork unitOfWork,
    IDocumentParserService parserService,
    ILogger<ImportQuestionsCommandHandler> logger)
    : IRequestHandler<ImportQuestionsCommand, QuestionImportResult>
{
    public async Task<QuestionImportResult> Handle(ImportQuestionsCommand request, CancellationToken ct)
    {
        var result = new QuestionImportResult();

        try
        {
         
            var subject = await unitOfWork.Subjects.GetByIdAsync(request.SubjectId, ct);
            if (subject == null)
            {
                result.ErrorMessages.Add("Фан ёфт нашуд.");
                return result;
            }

            List<ParsedQuestion> parsedQuestions;
            try
            {
                parsedQuestions = await parserService.ParseDocumentAsync(
                    request.FileContent, 
                    request.FileExtension, 
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error parsing document {FileName}", request.FileName);
                result.ErrorMessages.Add($"Хатогӣ ҳангоми хондани файл: {ex.Message}");
                return result;
            }

            result.TotalParsed = parsedQuestions.Count;

    
            foreach (var parsed in parsedQuestions)
            {
                try
                {
                    var exists = await unitOfWork.Questions.ExistsAsync(
                        request.SubjectId, 
                        parsed.QuestionText, 
                        ct);

                    if (exists)
                    {
                        result.Duplicates++;
                        continue;
                    }

                    var question = new Question
                    {
                        QuestionText = parsed.QuestionText,
                        SubjectId = request.SubjectId,
                        Option = new Option
                        {
                            OptionA = parsed.OptionA,
                            OptionB = parsed.OptionB,
                            OptionC = parsed.OptionC,
                            OptionD = parsed.OptionD,
                            CorrectAnswer = parsed.CorrectAnswer
                        }
                    };

                    await unitOfWork.Questions.AddAsync(question, ct);
                    result.SuccessfullyAdded++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error adding question: {QuestionText}", parsed.QuestionText);
                    result.Errors++;
                    result.ErrorMessages.Add($"Хатогӣ: {parsed.QuestionText.Substring(0, Math.Min(50, parsed.QuestionText.Length))}...");
                }
            }

            if (result.SuccessfullyAdded > 0)
            {
                await unitOfWork.SaveChangesAsync(ct);
            }

            logger.LogInformation("Question import completed: {Added} added, {Duplicates} duplicates, {Errors} errors", 
                result.SuccessfullyAdded, result.Duplicates, result.Errors);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error importing questions from {FileName}", request.FileName);
            result.ErrorMessages.Add("Хатогии умумӣ ҳангоми ворид кардани саволҳо.");
            return result;
        }
    }
}
