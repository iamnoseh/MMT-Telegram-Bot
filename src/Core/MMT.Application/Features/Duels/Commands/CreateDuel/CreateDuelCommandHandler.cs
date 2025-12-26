using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Duels.Commands.CreateDuel;

public class CreateDuelCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<CreateDuelCommandHandler> logger)
    : IRequestHandler<CreateDuelCommand, CreateDuelResult>
{
    public async Task<CreateDuelResult> Handle(CreateDuelCommand request, CancellationToken ct)
    {
        try
        {
            var challenger = await unitOfWork.Users.GetByChatIdAsync(request.ChallengerChatId, ct);
            if (challenger == null)
            {
                return new CreateDuelResult
                {
                    Success = false,
                    Message = "Шумо дар система вуҷуд надоред."
                };
            }

            // Generate unique duel code
            var duelCode = GenerateDuelCode();
            
            var duel = new Duel
            {
                ChallengerId = challenger.Id,
                OpponentId = null, // Will be set when opponent accepts
                SubjectId = request.SubjectId,
                DuelCode = duelCode,
                Status = DuelStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await unitOfWork.Duels.AddAsync(duel, ct);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("Duel created with code: {DuelCode} by {Challenger}",
                duelCode, challenger.Name);

            return new CreateDuelResult
            {
                Success = true,
                DuelCode = duelCode,
                Message = "Дуэл сохта шуд! Ссылкаро ба дӯстатон фиристед."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating duel");
            return new CreateDuelResult
            {
                Success = false,
                Message = "Хатогӣ ҳангоми сохтани дуэл."
            };
        }
    }
    
    private string GenerateDuelCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
