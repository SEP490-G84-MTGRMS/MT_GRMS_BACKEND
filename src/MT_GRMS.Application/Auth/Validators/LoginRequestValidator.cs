using FluentValidation;
using MT_GRMS.Application.Auth.Dtos;

namespace MT_GRMS.Application.Auth.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UsernameOrEmail).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.TenantCode).MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.TenantCode));
    }
}

public sealed class VerifyLoginOtpRequestValidator : AbstractValidator<VerifyLoginOtpRequest>
{
    public VerifyLoginOtpRequestValidator()
    {
        RuleFor(x => x.TempToken).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OtpCode).NotEmpty().Matches("^[0-9]{6}$");
    }
}

public sealed class RegisterTenantRequestValidator : AbstractValidator<RegisterTenantRequest>
{
    public RegisterTenantRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().MinimumLength(2).MaximumLength(100)
            .Matches(@"^[\p{L}][\p{L}\p{M}' -]*$");
        RuleFor(x => x.StoreName).NotEmpty().MinimumLength(2).MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^(03|05|07|08|09)\d{8}$");
        RuleFor(x => x.Email).NotEmpty().MaximumLength(100).EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(8).MaximumLength(64)
            .Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]")
            .Matches("[^A-Za-z0-9]")
            .Must(password => password is not null && !password.Any(char.IsWhiteSpace));
        RuleFor(x => x.AgreeTerms).Equal(true);
        RuleFor(x => x.OtpCode).NotEmpty().Matches("^[0-9]{6}$");
        RuleFor(x => x.ChallengeId).MaximumLength(128).When(x => !string.IsNullOrWhiteSpace(x.ChallengeId));
        RuleFor(x => x.IdempotencyKey).MaximumLength(128).When(x => !string.IsNullOrWhiteSpace(x.IdempotencyKey));
    }
}

public sealed class SendRegistrationOtpRequestValidator : AbstractValidator<SendRegistrationOtpRequest>
{
    public SendRegistrationOtpRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(100).EmailAddress();
        RuleFor(x => x.FullName).MinimumLength(2).MaximumLength(100)
            .Matches(@"^[\p{L}][\p{L}\p{M}' -]*$")
            .When(x => !string.IsNullOrWhiteSpace(x.FullName));
        RuleFor(x => x.StoreName).MinimumLength(2).MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.StoreName));
        RuleFor(x => x.Phone).Matches(@"^(03|05|07|08|09)\d{8}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

public sealed class VerifyRegistrationOtpRequestValidator : AbstractValidator<VerifyRegistrationOtpRequest>
{
    public VerifyRegistrationOtpRequestValidator()
    {
        RuleFor(x => x.EmailOrPhone).NotEmpty().MaximumLength(100).EmailAddress();
        RuleFor(x => x.OtpCode).NotEmpty().Matches("^[0-9]{6}$");
    }
}

internal static class AuthRequestValidation
{
    public static void EnsureValid<T>(IValidator<T> validator, T request)
    {
        var result = validator.Validate(request);
        if (!result.IsValid)
            throw new ArgumentException(string.Join(" ", result.Errors.Select(error => error.ErrorMessage)));
    }
}
