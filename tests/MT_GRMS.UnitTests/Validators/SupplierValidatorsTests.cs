using System;
using System.Collections.Generic;
using FluentValidation.TestHelper;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Application.Suppliers.Validators;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Validators;

public class SupplierValidatorsTests
{
    [Fact]
    public void AssignSupplierProductValidator_ShouldValidate()
    {
        var validator = new AssignSupplierProductValidator();

        var valid = new AssignSupplierProductCommand
        {
            ProductId = Guid.NewGuid(),
            DeliveryUoM = "Thùng 24 lon",
            AgreedWholesalePrice = 250000m,
            MinimumOrderQuantity = 5,
            AllowedShortageQuantity = 1,
            SupplierProductCode = "SUP-PR-01"
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new AssignSupplierProductCommand
        {
            ProductId = Guid.Empty,
            DeliveryUoM = new string('U', 51),
            AgreedWholesalePrice = -100m,
            MinimumOrderQuantity = 0,
            AllowedShortageQuantity = -1,
            SupplierProductCode = new string('C', 51)
        };
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.ProductId);
        res.ShouldHaveValidationErrorFor(x => x.DeliveryUoM);
        res.ShouldHaveValidationErrorFor(x => x.AgreedWholesalePrice);
        res.ShouldHaveValidationErrorFor(x => x.MinimumOrderQuantity);
        res.ShouldHaveValidationErrorFor(x => x.AllowedShortageQuantity);
        res.ShouldHaveValidationErrorFor(x => x.SupplierProductCode);
    }

    [Fact]
    public void UpdateSupplierProductValidator_ShouldValidate()
    {
        var validator = new UpdateSupplierProductValidator();

        var valid = new UpdateSupplierProductCommand
        {
            DeliveryUoM = "Hộp",
            AgreedWholesalePrice = 10000m,
            MinimumOrderQuantity = 1,
            AllowedShortageQuantity = 0
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new UpdateSupplierProductCommand
        {
            DeliveryUoM = new string('U', 51),
            AgreedWholesalePrice = -50m,
            MinimumOrderQuantity = 0,
            AllowedShortageQuantity = -2
        };
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.DeliveryUoM);
        res.ShouldHaveValidationErrorFor(x => x.AgreedWholesalePrice);
        res.ShouldHaveValidationErrorFor(x => x.MinimumOrderQuantity);
        res.ShouldHaveValidationErrorFor(x => x.AllowedShortageQuantity);
    }

    [Fact]
    public void CreateSupplierCommandValidator_ShouldValidate()
    {
        var validator = new CreateSupplierCommandValidator();

        var valid = new CreateSupplierCommand
        {
            Name = "Vinamilk Supplier",
            Phone = "0987654321",
            Status = SupplierStatus.Active,
            Email = "contact@vinamilk.vn",
            TaxCode = "0300588569",
            PaymentTermDays = 30,
            CreditLimit = 50000000m
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new CreateSupplierCommand
        {
            Name = "   ",
            Phone = "",
            Status = (SupplierStatus)99,
            Email = "invalid-email",
            TaxCode = new string('T', 51),
            PaymentTermDays = -1,
            CreditLimit = -100m
        };
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.Name);
        res.ShouldHaveValidationErrorFor(x => x.Phone);
        res.ShouldHaveValidationErrorFor(x => x.Status);
        res.ShouldHaveValidationErrorFor(x => x.Email);
        res.ShouldHaveValidationErrorFor(x => x.TaxCode);
        res.ShouldHaveValidationErrorFor(x => x.PaymentTermDays);
        res.ShouldHaveValidationErrorFor(x => x.CreditLimit);
    }

    [Fact]
    public void UpdateBasicSupplierCommandValidator_ShouldValidate()
    {
        var validator = new UpdateBasicSupplierCommandValidator();

        var valid = new UpdateBasicSupplierCommand
        {
            Name = "Updated Supplier",
            Phone = "0912345678",
            Status = SupplierStatus.Active,
            ContactPerson = "Manager",
            TaxCode = "0300588569",
            Address = "123 Street",
            Email = "sup@example.com",
            Notes = "Good"
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new UpdateBasicSupplierCommand
        {
            Name = "",
            Phone = "  ",
            Status = (SupplierStatus)99,
            ContactPerson = new string('C', 101),
            TaxCode = new string('T', 51),
            Address = new string('A', 301),
            Email = "bad-email",
            Notes = new string('N', 501)
        };
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.Name);
        res.ShouldHaveValidationErrorFor(x => x.Phone);
        res.ShouldHaveValidationErrorFor(x => x.Status);
        res.ShouldHaveValidationErrorFor(x => x.ContactPerson);
        res.ShouldHaveValidationErrorFor(x => x.TaxCode);
        res.ShouldHaveValidationErrorFor(x => x.Address);
        res.ShouldHaveValidationErrorFor(x => x.Email);
        res.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void UpdateSupplierBankCommandValidator_ShouldValidate()
    {
        var validator = new UpdateSupplierBankCommandValidator();

        var valid = new UpdateSupplierBankCommand
        {
            BankName = "Vietcombank",
            BankAccountNumber = "1234567890",
            BankAccountHolder = "NGUYEN VAN A"
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var emptyWhenNoBank = new UpdateSupplierBankCommand();
        validator.TestValidate(emptyWhenNoBank).ShouldNotHaveAnyValidationErrors();

        var invalidNumber = new UpdateSupplierBankCommand
        {
            BankName = "Techcombank",
            BankAccountNumber = "1234-ABCD", // Chứa ký tự không phải số
            BankAccountHolder = "LE VAN B"
        };
        validator.TestValidate(invalidNumber).ShouldHaveValidationErrorFor(x => x.BankAccountNumber);
    }

    [Fact]
    public void UpdateSupplierVatCommandValidator_ShouldValidate()
    {
        var validator = new UpdateSupplierVatCommandValidator();

        var valid = new UpdateSupplierVatCommand
        {
            CompanyName = "Cong Ty Co Phan ABC",
            TaxCode = "0101234567",
            InvoiceAddress = "123 Pho Hue, Hanoi"
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalidTaxCode = new UpdateSupplierVatCommand
        {
            CompanyName = "Cong Ty ABC",
            TaxCode = "12345", // Sai định dạng MST
            InvoiceAddress = new string('A', 301)
        };
        var res = validator.TestValidate(invalidTaxCode);
        res.ShouldHaveValidationErrorFor(x => x.TaxCode);
        res.ShouldHaveValidationErrorFor(x => x.InvoiceAddress);
    }

    [Fact]
    public void UpdateSupplierCommandValidator_ShouldValidate()
    {
        var validator = new UpdateSupplierCommandValidator();

        var valid = new UpdateSupplierCommand
        {
            Name = "Supplier Name",
            Phone = "0987654321",
            Status = SupplierStatus.Active,
            Email = "test@sup.vn",
            PaymentTermDays = 15,
            CreditLimit = 10000000m
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new UpdateSupplierCommand
        {
            Name = "",
            Phone = "",
            Status = (SupplierStatus)99,
            Email = "not-valid",
            PaymentTermDays = -1,
            CreditLimit = -5m
        };
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.Name);
        res.ShouldHaveValidationErrorFor(x => x.Phone);
        res.ShouldHaveValidationErrorFor("Status.Value");
        res.ShouldHaveValidationErrorFor(x => x.Email);
        res.ShouldHaveValidationErrorFor(x => x.PaymentTermDays);
        res.ShouldHaveValidationErrorFor(x => x.CreditLimit);
    }

    [Fact]
    public void RecordAPTransactionCommandValidator_ShouldValidate()
    {
        var validator = new RecordAPTransactionCommandValidator();

        var valid = new RecordAPTransactionCommand
        {
            SupplierId = Guid.NewGuid(),
            DocType = APDocType.PurchaseInvoice,
            DocNo = "INV-001",
            Amount = 5000000m,
            Direction = 1,
            PaymentMethod = "BANK_TRANSFER",
            ReferenceNo = "REF-001",
            Description = "Goods receipt payment"
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new RecordAPTransactionCommand
        {
            SupplierId = Guid.Empty,
            DocType = APDocType.PurchaseInvoice,
            DocNo = "", // Required when not PaymentVoucher
            Amount = 0m,
            Direction = 2, // Must be 1 or -1
            PaymentMethod = "CRYPTO", // Not allowed
            ReferenceNo = new string('R', 101),
            Description = new string('D', 501)
        };
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.SupplierId);
        res.ShouldHaveValidationErrorFor(x => x.DocNo);
        res.ShouldHaveValidationErrorFor(x => x.Amount);
        res.ShouldHaveValidationErrorFor(x => x.Direction);
        res.ShouldHaveValidationErrorFor(x => x.PaymentMethod);
        res.ShouldHaveValidationErrorFor(x => x.ReferenceNo);
        res.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void UpdateSupplierDeliveryConfigCommandValidator_ShouldValidate()
    {
        var validator = new UpdateSupplierDeliveryConfigCommandValidator();

        var valid = new UpdateSupplierDeliveryConfigCommand
        {
            CutoffDayOffset = 1,
            MinOrderQuantity = 5,
            CutoffTime = "17:30",
            DeliveryDays = new List<string> { "Monday", "Wednesday" },
            UrgentFeePercent = 10m
        };
        validator.TestValidate(valid).ShouldNotHaveAnyValidationErrors();

        var invalid = new UpdateSupplierDeliveryConfigCommand
        {
            CutoffDayOffset = -1,
            MinOrderQuantity = -1,
            CutoffTime = "25:00", // Invalid HH:mm
            DeliveryDays = new List<string>(), // Empty
            UrgentFeePercent = 150m // > 100
        };
        var res = validator.TestValidate(invalid);
        res.ShouldHaveValidationErrorFor(x => x.CutoffDayOffset);
        res.ShouldHaveValidationErrorFor(x => x.MinOrderQuantity);
        res.ShouldHaveValidationErrorFor(x => x.CutoffTime);
        res.ShouldHaveValidationErrorFor(x => x.DeliveryDays);
        res.ShouldHaveValidationErrorFor(x => x.UrgentFeePercent);
    }
}
