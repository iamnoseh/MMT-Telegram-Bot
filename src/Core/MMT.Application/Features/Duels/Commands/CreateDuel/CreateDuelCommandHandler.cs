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

            var opponent = await unitOfWork.Users.GetByChatIdAsync(request.OpponentChatId, ct);
            if (opponent == null)
            {
                return new CreateDuelResult
                {
                    Success = false,
                    Message = "Ҳарифи интихобшуда дар система вуҷуд надорад."
                };
            }

            if (challenger.Id == opponent.Id)
            {
                return new CreateDuelResult
                {
                    Success = false,
                    Message = "Шумо наметавонед бо худатон дуэл созед!"
                };
            }

            var duel = new Duel
            {
                ChallengerId = challenger.Id,
                OpponentId = opponent.Id,
                SubjectId = request.SubjectId,
                Status = DuelStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await unitOfWork.Duels.AddAsync(duel, ct);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("Duel created: {DuelId} between {Challenger} and {Opponent}",
                duel.Id, challenger.Name, opponent.Name);

            return new CreateDuelResult
            {
                Success = true,
                DuelId = duel.Id,
                Message = $"Дуэл бо {opponent.Name} сохта шуд!"
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
}
