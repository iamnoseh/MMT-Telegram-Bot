using MediatR;
using MMT.Application.Features.Users.DTOs;

namespace MMT.Application.Features.Users.Queries.GetTopUsers;

public record GetTopUsersQuery(int Count = 10) : IRequest<List<UserDto>>;
