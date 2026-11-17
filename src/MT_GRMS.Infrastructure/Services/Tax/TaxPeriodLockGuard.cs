using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Infrastructure.Services.Tax;

/// <summary>
/// Thực thi kiểm tra khóa sổ kỳ tính thuế (TaxPeriodLockGuard).
/// Chặn mọi tác vụ sửa đổi, hủy hoặc đảo chứng từ thuộc kỳ tính thuế đã được chốt sổ bất biến.
/// </summary>
public class TaxPeriodLockGuard : ITaxPeriodLockGuard
{
    private readonly IHouseholdTaxRepository _taxRepository;

    public TaxPeriodLockGuard(IHouseholdTaxRepository taxRepository)
    {
        _taxRepository = taxRepository ?? throw new ArgumentNullException(nameof(taxRepository));
    }

    /// <inheritdoc />
    public async Task<bool> IsPeriodFinalizedAsync(
        DateTime dateUtc,
        Guid? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await FindFinalizedSnapshotAsync(dateUtc, branchId, cancellationToken);
        return snapshot != null;
    }

    /// <inheritdoc />
    public async Task ValidateTaxPeriodUnlockedAsync(
        DateTime dateUtc,
        string actionDescription,
        Guid? branchId = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await FindFinalizedSnapshotAsync(dateUtc, branchId, cancellationToken);
        if (snapshot != null)
        {
            var periodDesc = snapshot.PeriodType switch
            {
                TaxPeriodType.Month => $"Tháng {snapshot.PeriodNumber}/{snapshot.Year}",
                TaxPeriodType.Quarter => $"Quý {snapshot.PeriodNumber}/{snapshot.Year}",
                TaxPeriodType.Year => $"Năm {snapshot.Year}",
                _ => $"{snapshot.PeriodType} {snapshot.PeriodNumber}/{snapshot.Year}"
            };

            throw new BusinessException(
                "TAX_PERIOD_FINALIZED",
                $"Kỳ tính thuế {periodDesc} đã được chốt sổ bất biến. Không thể {actionDescription}. Mọi sai sót sau khi chốt sổ phải thực hiện điều chỉnh ở kỳ tiếp theo theo quy định kế toán.");
        }
    }

    /// <summary>
    /// Chuyển đổi mốc thời gian UTC sang múi giờ Việt Nam (UTC+7) để xác định chính xác Năm, Tháng, Quý,
    /// sau đó truy vấn snapshot chốt kỳ tương ứng.
    /// Phạm vi: Nếu kỳ chốt ở cấp Toàn Hộ Kinh Doanh (BranchId == null), tất cả chi nhánh đều bị khóa.
    /// </summary>
    private async Task<HouseholdTaxSnapshot?> FindFinalizedSnapshotAsync(
        DateTime dateUtc,
        Guid? branchId,
        CancellationToken cancellationToken)
    {
        var vnTz = HouseholdTaxService.GetVietnamTimeZone();
        var utc = dateUtc.Kind switch
        {
            DateTimeKind.Utc => dateUtc,
            DateTimeKind.Local => dateUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateUtc, DateTimeKind.Utc)
        };

        var localDate = TimeZoneInfo.ConvertTimeFromUtc(utc, vnTz);
        var year = localDate.Year;
        var month = localDate.Month;
        var quarter = (month - 1) / 3 + 1;

        var snapshots = await _taxRepository.GetSnapshotsAsync(year, null, cancellationToken);
        if (snapshots == null || snapshots.Count == 0)
        {
            return null;
        }

        // Phạm vi: Kỳ tính thuế đã chốt là khóa toàn bộ hộ kinh doanh (Tenant-wide) trên tất cả chi nhánh
        return snapshots.FirstOrDefault(s =>
            s.Status == TaxSnapshotStatus.Finalized &&
            (s.Year == year) &&
            (
                (s.PeriodType == TaxPeriodType.Month && s.PeriodNumber == month) ||
                (s.PeriodType == TaxPeriodType.Quarter && s.PeriodNumber == quarter) ||
                (s.PeriodType == TaxPeriodType.Year)
            )
        );
    }
}
