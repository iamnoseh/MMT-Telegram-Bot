using MediatR;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Application.Features.Users.DTOs;

namespace MMT.Application.Features.Users.Queries.GetUserByChatId;

public class GetUserByChatIdQueryHandler(IUnitOfWork unitOfWork) 
    : IRequestHandler<GetUserByChatIdQuery, UserDto?>
{
    public async Task<UserDto?> Handle(GetUserByChatIdQuery request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
        
        if (user == null) return null;
        
        return new UserDto
        {
            Id = user.Id,
            ChatId = user.ChatId,
            Username = user.Username,
            Name = user.Name,
            City = user.City,
            Score = user.Score,
            IsAdmin = user.IsAdmin
        };
    }
}
