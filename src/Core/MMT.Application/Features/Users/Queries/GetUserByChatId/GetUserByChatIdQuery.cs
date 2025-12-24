using MediatR;
using MMT.Application.Features.Users.DTOs;

namespace MMT.Application.Features.Users.Queries.GetUserByChatId;

public record GetUserByChatIdQuery(long ChatId) : IRequest<UserDto?>;
