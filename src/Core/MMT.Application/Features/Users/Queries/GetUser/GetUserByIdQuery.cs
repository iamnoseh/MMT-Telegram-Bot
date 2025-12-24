using MediatR;
using MMT.Application.Features.Users.DTOs;

namespace MMT.Application.Features.Users.Queries.GetUser;

public record GetUserByIdQuery(int UserId) : IRequest<UserDto?>;
