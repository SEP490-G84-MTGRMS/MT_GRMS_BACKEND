using System;
using System.Collections.Generic;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class SupplierReturnPolicyRulesTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("not-a-json", 0)]
    [InlineData("[\"A\", \"B\"]", 2)]
    public void ParseJsonList_HandlesVariousInputs(string? input, int expectedCount)
    {
        var result = SupplierReturnPolicyRules.ParseJsonList(input);
        Assert.Equal(expectedCount, result.Count);
    }

    [Theory]
    [InlineData(null, "REDUCE_PAYABLE")]
    [InlineData("", "REDUCE_PAYABLE")]
    [InlineData("  ", "REDUCE_PAYABLE")]
    [InlineData("REPLACE_GOODS", "EXCHANGE")]
    [InlineData("exchange", "EXCHANGE")]
    [InlineData("DEDUCT_INVOICE", "REDUCE_PAYABLE")]
    [InlineData("reduce_payable", "REDUCE_PAYABLE")]
    [InlineData("REFUND", "REFUND")]
    [InlineData("SUPPLIER_CREDIT", "SUPPLIER_CREDIT")]
    [InlineData("OTHER", "OTHER")]
    [InlineData("CUSTOM_TYPE", "CUSTOM_TYPE")]
    public void NormalizeResolutionType_ReturnsExpected(string? input, string expected)
    {
        var result = SupplierReturnPolicyRules.NormalizeResolutionType(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("REFUND", false)]
    [InlineData("EXCHANGE", true)]
    [InlineData("auto_exchange_po", true)]
    public void IsExchangeResolution_ReturnsExpected(string? input, bool expected)
    {
        Assert.Equal(expected, SupplierReturnPolicyRules.IsExchangeResolution(input));
    }

    [Theory]
    [InlineData("DAMAGED", null, "QUALITY_DEFECT")]
    [InlineData("DEFECTIVE", null, "QUALITY_DEFECT")]
    [InlineData("QUALITY_DEFECT", null, "QUALITY_DEFECT")]
    [InlineData("NEAR_EXPIRY_RETURN", null, "NEAR_EXPIRY")]
    [InlineData("NEAR_EXPIRY", null, "NEAR_EXPIRY")]
    [InlineData("EXPIRY", null, "EXPIRED")]
    [InlineData("EXPIRED", null, "EXPIRED")]
    [InlineData("WRONG_SPEC", null, "WRONG_ITEM")]
    [InlineData("WRONG_SPEC_DISCOVERED", null, "WRONG_ITEM")]
    [InlineData("WRONG_ITEM", null, "WRONG_ITEM")]
    [InlineData("RECALLED", null, "RECALL")]
    [InlineData("RECALL", null, "RECALL")]
    [InlineData("OTHER", null, "OTHER")]
    [InlineData("UNKNOWN_CODE", null, "UNKNOWN_CODE")]
    [InlineData(null, "Sữa đã hết hạn rồi", "EXPIRED")]
    [InlineData(null, "Hàng cận hạn date ngắn", "NEAR_EXPIRY")]
    [InlineData(null, "Sản phẩm bị thu hồi theo công văn", "RECALL")]
    [InlineData(null, "Giao sai mẫu mã", "WRONG_ITEM")]
    [InlineData(null, "Hộp móp méo hư hỏng lỗi", "QUALITY_DEFECT")]
    [InlineData(null, "Lý do cá nhân không thích", "OTHER")]
    public void NormalizeReturnReasonType_ReturnsExpected(string? reasonType, string? freeText, string expected)
    {
        var result = SupplierReturnPolicyRules.NormalizeReturnReasonType(reasonType, freeText);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EnsureReturnAllowed_DisallowReturn_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy { AllowReturn = false };
        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureReturnAllowed(policy, "REDUCE_PAYABLE", "EXPIRED"));
        Assert.Equal("RETURN_NOT_ALLOWED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureReturnAllowed_DisallowedReturnType_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy
        {
            AllowReturn = true,
            AllowedReturnTypesJson = "[\"QUALITY_DEFECT\"]"
        };
        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureReturnAllowed(policy, "REDUCE_PAYABLE", "EXPIRED"));
        Assert.Equal("RETURN_TYPE_NOT_ALLOWED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureReturnAllowed_DisallowedResolutionType_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy
        {
            AllowReturn = true,
            AllowedReturnTypesJson = "[\"EXPIRED\"]",
            AllowedResolutionTypesJson = "[\"EXCHANGE\"]"
        };
        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureReturnAllowed(policy, "REFUND", "EXPIRED"));
        Assert.Equal("RESOLUTION_NOT_ALLOWED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureReturnAllowed_WindowExpired_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy
        {
            AllowReturn = true,
            MaxReturnDaysAfterReceipt = 7
        };
        var receiptDate = DateTime.UtcNow.AddDays(-10);

        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureReturnAllowed(policy, "EXCHANGE", "QUALITY_DEFECT", receiptDateUtc: receiptDate));
        Assert.Equal("RETURN_WINDOW_EXPIRED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureReturnAllowed_EvidenceMissing_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy
        {
            AllowReturn = true,
            RequireEvidenceImage = true
        };

        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureReturnAllowed(policy, "EXCHANGE", "QUALITY_DEFECT", hasEvidence: false));
        Assert.Equal("EVIDENCE_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureReturnAllowed_NullPolicy_UsesDefaultsAndSucceeds()
    {
        // When policy is null, default types & resolutions are allowed
        SupplierReturnPolicyRules.EnsureReturnAllowed(
            null,
            "EXCHANGE",
            "QUALITY_DEFECT",
            receiptDateUtc: DateTime.UtcNow.AddDays(-30),
            hasEvidence: true);
    }

    [Fact]
    public void EnsureDispositionAllowed_DisallowedReturn_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy { AllowReturn = false };
        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureDispositionAllowed(policy, "DAMAGED"));
        Assert.Equal("RETURN_NOT_ALLOWED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureDispositionAllowed_DisallowedType_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy
        {
            AllowReturn = true,
            AllowedReturnTypesJson = "[\"QUALITY_DEFECT\"]"
        };
        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureDispositionAllowed(policy, "EXPIRED"));
        Assert.Equal("RETURN_TYPE_NOT_ALLOWED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureDispositionAllowed_WindowExpired_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy
        {
            AllowReturn = true,
            MaxReturnDaysAfterReceipt = 3
        };
        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureDispositionAllowed(policy, "NEAR_EXPIRY", receiptDateUtc: DateTime.UtcNow.AddDays(-5)));
        Assert.Equal("RETURN_WINDOW_EXPIRED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureDispositionAllowed_EvidenceRequired_ThrowsBusinessException()
    {
        var policy = new SupplierReturnPolicy
        {
            AllowReturn = true,
            RequireEvidenceImage = true
        };
        var ex = Assert.Throws<BusinessException>(() =>
            SupplierReturnPolicyRules.EnsureDispositionAllowed(policy, "QUALITY_DEFECT", hasEvidence: false));
        Assert.Equal("EVIDENCE_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public void EnsureDispositionAllowed_Valid_Succeeds()
    {
        SupplierReturnPolicyRules.EnsureDispositionAllowed(null, "DAMAGED", DateTime.UtcNow, true);
    }
}

