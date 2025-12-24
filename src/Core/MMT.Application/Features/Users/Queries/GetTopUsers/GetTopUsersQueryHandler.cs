using MediatR;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Application.Features.Users.DTOs;

namespace MMT.Application.Features.Users.Queries.GetTopUsers;

public class GetTopUsersQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetTopUsersQuery, List<UserDto>>
{
    public async Task<List<UserDto>> Handle(GetTopUsersQuery request, CancellationToken ct)
    {
        var users = await unitOfWork.Users.GetTopByScoreAsync(request.Count, ct);
        
        return users.Select(u => new UserDto
        {
            Id = u.Id,
            ChatId = u.ChatId,
            Username = u.Username,
            Name = u.Name,
            City = u.City,
            Score = u.Score,
            IsAdmin = u.IsAdmin
        }).ToList();
    }
}
