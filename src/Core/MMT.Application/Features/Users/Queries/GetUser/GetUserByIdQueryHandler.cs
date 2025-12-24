using MediatR;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Application.Features.Users.DTOs;

namespace MMT.Application.Features.Users.Queries.GetUser;

public class GetUserByIdQueryHandler(IUnitOfWork unitOfWork) 
    : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    public async Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByIdAsync(request.UserId, ct);
        
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
