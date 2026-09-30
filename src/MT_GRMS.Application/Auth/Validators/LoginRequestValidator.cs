using FluentValidation;
using MT_GRMS.Application.Auth.Dtos;

namespace MT_GRMS.Application.Auth.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
    }
}
