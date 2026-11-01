using MT_GRMS.Application.Tax.Security;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests;

public class TaxSnapshotSignatureTests
{
    private readonly TaxSnapshotSignatureService _signatureService = new();

    [Fact]
    public void ComputeSignature_And_VerifySignature_UntamperedData_ShouldBeValid()
    {
        // Arrange
        var snapshot = new HouseholdTaxSnapshot
        {
            BranchId = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            TaxableRevenueInPeriod = 467_500_000m,
            TotalTaxAmount = 6_762_500m,
            FinalizedAtUtc = new DateTime(2026, 10, 28, 16, 0, 0, DateTimeKind.Utc),
            FinalizedBy = "store_owner_01"
        };

        // Act: Ký số
        var signature = _signatureService.ComputeSignature(snapshot);
        snapshot.AuditSignatureHash = signature;

        // Assert: Xác minh
        var isValid = _signatureService.VerifySignature(snapshot);
        Assert.True(isValid);
        Assert.False(string.IsNullOrWhiteSpace(signature));
    }

    [Fact]
    public void VerifySignature_WhenTotalTaxAmountTampered_ShouldReturnFalse()
    {
        // Arrange
        var snapshot = new HouseholdTaxSnapshot
        {
            BranchId = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            TaxableRevenueInPeriod = 467_500_000m,
            TotalTaxAmount = 6_762_500m,
            FinalizedAtUtc = new DateTime(2026, 10, 28, 16, 0, 0, DateTimeKind.Utc),
            FinalizedBy = "store_owner_01"
        };
        snapshot.AuditSignatureHash = _signatureService.ComputeSignature(snapshot);

        // Act: Kẻ gian can thiệp sửa đổi số tiền thuế trong DB từ 6.762.500đ xuống 1.000.000đ
        snapshot.TotalTaxAmount = 1_000_000m;

        // Assert: Phát hiện gian lận tính toàn vẹn
        var isValid = _signatureService.VerifySignature(snapshot);
        Assert.False(isValid);
    }

    [Fact]
    public void VerifySignature_WhenFinalizedByTampered_ShouldReturnFalse()
    {
        // Arrange
        var snapshot = new HouseholdTaxSnapshot
        {
            BranchId = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 3,
            TaxableRevenueInPeriod = 467_500_000m,
            TotalTaxAmount = 6_762_500m,
            FinalizedAtUtc = new DateTime(2026, 10, 28, 16, 0, 0, DateTimeKind.Utc),
            FinalizedBy = "legit_owner"
        };
        snapshot.AuditSignatureHash = _signatureService.ComputeSignature(snapshot);

        // Act: Giả mạo người chốt
        snapshot.FinalizedBy = "hacker";

        // Assert
        var isValid = _signatureService.VerifySignature(snapshot);
        Assert.False(isValid);
    }

    [Fact]
    public void VerifySignature_WithKeyRotation_ShouldVerifyHistoricSnapshotsCorrectly()
    {
        // 1. Snapshot được ký với KeyId mặc định "k-2026-v1"
        var snapshot = new HouseholdTaxSnapshot
        {
            BranchId = Guid.NewGuid(),
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            TaxableRevenueInPeriod = 500_000_000m,
            TotalTaxAmount = 7_500_000m,
            FinalizedAtUtc = new DateTime(2026, 6, 30, 16, 0, 0, DateTimeKind.Utc),
            FinalizedBy = "owner_2026"
        };

        var signature = _signatureService.ComputeSignature(snapshot);
        snapshot.AuditSignatureHash = signature;

        // Chữ ký phải có tiền tố KeyId
        Assert.StartsWith("k-2026-v1:", signature);

        // Xác minh thành công
        Assert.True(_signatureService.VerifySignature(snapshot));

        // 2. Kẻ gian giả mạo KeyId không tồn tại trong hệ thống
        snapshot.AuditSignatureHash = "unknown-key-9999:" + signature.Split(':')[1];
        Assert.False(_signatureService.VerifySignature(snapshot));
    }
}