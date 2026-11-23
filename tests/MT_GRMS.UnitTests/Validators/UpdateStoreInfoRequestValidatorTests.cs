using FluentValidation.TestHelper;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Validators;

namespace MT_GRMS.UnitTests.Validators;

public class UpdateStoreInfoRequestValidatorTests
{
    private readonly UpdateStoreInfoRequestValidator _validator = new();
    private static readonly UpdateTenantRequest ValidRequest = new("Cửa hàng", null, null, null, null);

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void Name_IsRequired(string? name)
    {
        _validator.TestValidate(ValidRequest with { Name = name! })
            .ShouldHaveValidationErrorFor(request => request.Name).Only();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(149, true)]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void Name_ChecksTrimmedLength(int length, bool expected)
    {
        var request = ValidRequest with { Name = $" {new string('ệ', length)} " };
        Assert.Equal(expected, _validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" \t ", true)]
    [InlineData("01234567", false)]
    [InlineData("012345678", true)]
    [InlineData(" +01234567890 ", true)]
    [InlineData("012345678901", true)]
    [InlineData("+012345678901", true)]
    [InlineData("0123456789012", false)]
    [InlineData("+0123456789012", false)]
    [InlineData("01234a6789", false)]
    [InlineData("01234 56789", false)]
    [InlineData("01234-56789", false)]
    [InlineData("01234+56789", false)]
    [InlineData("++0123456789", false)]
    [InlineData("０１２３４５６７８９", false)]
    public void Hotline_IsOptionalAndChecksDigitCountAndFormat(string? hotline, bool expected)
    {
        var request = ValidRequest with { Hotline = hotline };
        var result = _validator.TestValidate(request);

        if (expected)
        {
            result.ShouldNotHaveAnyValidationErrors();
        }
        else
        {
            result.ShouldHaveValidationErrorFor(value => value.Hotline).Only();
        }
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("A")]
    public void TaxCodeCityAndAddress_AcceptEmptyOrOneCharacter(string? value)
    {
        _validator.TestValidate(ValidRequest with { TaxCode = value, City = value, Address = value })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void TaxCodeCityAndAddress_CheckTrimmedLengthAroundColumnLimits(int offset)
    {
        var request = ValidRequest with
        {
            TaxCode = $" {new string('0', 50 + offset)} ",
            City = $" {new string('ệ', 100 + offset)} ",
            Address = $" {new string('ệ', 255 + offset)} "
        };
        var result = _validator.TestValidate(request);

        if (offset > 0)
        {
            result.ShouldHaveValidationErrorFor(value => value.TaxCode);
            result.ShouldHaveValidationErrorFor(value => value.City);
            result.ShouldHaveValidationErrorFor(value => value.Address);
        }
        else
        {
            result.ShouldNotHaveAnyValidationErrors();
        }
    }
}
