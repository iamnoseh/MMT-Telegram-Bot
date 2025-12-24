using FluentValidation;

namespace MMT.Application.Features.Users.Commands.RegisterUser;

public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty()
            .WithMessage("ChatId лозим аст");
            
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Ном лозим аст")
            .MaximumLength(100)
            .WithMessage("Ном аз 100 аломат зиёд набояд бошад");
            
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .WithMessage("Рақами телефон лозим аст")
            .Matches(@"^\+?[1-9]\d{1,14}$")
            .WithMessage("Формати рақами телефон нодуруст");
            
        RuleFor(x => x.City)
            .NotEmpty()
            .WithMessage("Шаҳр лозим аст")
            .MaximumLength(50)
            .WithMessage("Номи шаҳр аз 50 аломат зиёд набояд бошад");
    }
}
