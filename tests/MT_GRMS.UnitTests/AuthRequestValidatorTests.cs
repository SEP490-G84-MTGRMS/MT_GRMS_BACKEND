using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Auth.Validators;

namespace MT_GRMS.UnitTests;

public class AuthRequestValidatorTests
{
    [Fact]
    public void Login_rejects_oversized_identifier_and_password()
    {
        var validator = new LoginRequestValidator();

        Assert.False(validator.Validate(new LoginRequest(new string('a', 255), "Password@123", null)).IsValid);
        Assert.False(validator.Validate(new LoginRequest("owner@example.com", new string('a', 129), null)).IsValid);
        Assert.True(validator.Validate(new LoginRequest("owner@example.com", "legacy1", null)).IsValid);
    }

    [Theory]
    [InlineData("password@1")]
    [InlineData("PASSWORD@1")]
    [InlineData("Password@@")]
    [InlineData("Password11")]
    [InlineData("Pass word@1")]
    [InlineData("Short@1")]
    public void Register_rejects_weak_password(string password)
    {
        var request = ValidRegister() with { Password = password };
        Assert.False(new RegisterTenantRequestValidator().Validate(request).IsValid);
    }

    [Theory]
    [InlineData("0212345678")]
    [InlineData("091234567")]
    [InlineData("+84912345678")]
    public void Register_rejects_non_canonical_phone(string phone)
    {
        Assert.False(new RegisterTenantRequestValidator().Validate(ValidRegister() with { Phone = phone }).IsValid);
    }

    [Fact]
    public void Register_requires_explicit_terms_and_valid_boundaries()
    {
        var validator = new RegisterTenantRequestValidator();

        Assert.False(validator.Validate(ValidRegister() with { AgreeTerms = false }).IsValid);
        Assert.False(validator.Validate(ValidRegister() with { FullName = "A" }).IsValid);
        Assert.False(validator.Validate(ValidRegister() with { StoreName = new string('S', 151) }).IsValid);
        Assert.False(validator.Validate(ValidRegister() with { Email = new string('a', 91) + "@mail.com" }).IsValid);
        Assert.True(validator.Validate(ValidRegister()).IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abc123")]
    [InlineData("１２３４５６")]
    public void Otp_requires_six_ascii_digits(string otp)
    {
        Assert.False(new VerifyLoginOtpRequestValidator().Validate(new VerifyLoginOtpRequest("otp_login_token", otp)).IsValid);
        Assert.False(new VerifyRegistrationOtpRequestValidator().Validate(new VerifyRegistrationOtpRequest("owner@example.com", otp)).IsValid);
    }

    private static RegisterTenantRequest ValidRegister() => new(
        "Nguyễn Văn A", "Cửa hàng A", "0912345678", "owner@example.com",
        "Password@1", true, "123456", false, "idem-key", "challenge-key");
}
