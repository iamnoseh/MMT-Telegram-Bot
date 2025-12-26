using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Duels.Commands.AcceptDuel;

public class AcceptDuelCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<AcceptDuelCommandHandler> logger)
    : IRequestHandler<AcceptDuelCommand, AcceptDuelResult>
{
    public async Task<AcceptDuelResult> Handle(AcceptDuelCommand request, CancellationToken ct)
    {
        try
        {
            var duel = await unitOfWork.Duels.GetByIdWithDetailsAsync(request.DuelId, ct);
            if (duel == null)
            {
                return new AcceptDuelResult
                {
                    Success = false,
                    Message = "Дуэл ёфт нашуд."
                };
            }

            var opponent = await unitOfWork.Users.GetByChatIdAsync(request.OpponentChatId, ct);
            if (opponent == null || duel.OpponentId != opponent.Id)
            {
                return new AcceptDuelResult
                {
                    Success = false,
                    Message = "Шумо иҷозати қабул кардани ин дуэлро надоред."
                };
            }

            if (duel.Status != DuelStatus.Pending)
            {
                return new AcceptDuelResult
                {
                    Success = false,
                    Message = "Ин дуэл аллакай қабул ё рад карда шудааст."
                };
            }

            duel.Status = DuelStatus.Active;
            unitOfWork.Duels.Update(duel);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("Duel {DuelId} accepted by {OpponentId}", duel.Id, opponent.Id);

            return new AcceptDuelResult
            {
                Success = true,
                SubjectId = duel.SubjectId,
                Message = $"Дуэл бо {duel.Challenger.Name} қабул шуд!"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error accepting duel {DuelId}", request.DuelId);
            return new AcceptDuelResult
            {
                Success = false,
                Message = "Хатогӣ ҳангоми қабули дуэл."
            };
        }
    }
}
