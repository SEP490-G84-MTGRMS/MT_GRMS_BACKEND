using System;
using System.Threading;
using System.Threading.Tasks;

namespace MT_GRMS.Application.Tax;

/// <summary>
/// Cơ chế bảo vệ và kiểm soát tính bất biến của kỳ tính thuế đã chốt.
/// Chặn các hành vi ghi đè, đảo bút toán hoặc hủy chứng từ khi ngày phát sinh rơi vào kỳ đã khóa sổ.
/// </summary>
public interface ITaxPeriodLockGuard
{
    /// <summary>
    /// Kiểm tra xem một mốc thời gian (UTC) có rơi vào kỳ tính thuế đã chốt sổ hay không.
    /// Phạm vi chốt sổ mặc định là toàn bộ hộ kinh doanh (Tenant-wide, BranchId == null).
    /// </summary>
    /// <param name="dateUtc">Mốc thời gian phát sinh chứng từ (UTC).</param>
    /// <param name="branchId">Mã chi nhánh phát sinh chứng từ (tùy chọn).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True nếu kỳ thuế chứa ngày này đã bị chốt sổ; ngược lại False.</returns>
    Task<bool> IsPeriodFinalizedAsync(
        DateTime dateUtc,
        Guid? branchId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xác thực kỳ tính thuế chứa mốc thời gian (UTC) vẫn đang mở.
    /// Ném BusinessException("TAX_PERIOD_FINALIZED", ...) nếu kỳ thuế đã bị chốt và khóa sổ.
    /// </summary>
    /// <param name="dateUtc">Mốc thời gian phát sinh chứng từ (UTC).</param>
    /// <param name="actionDescription">Mô tả hành động bị chặn (ví dụ: 'hủy hóa đơn', 'tạo phiếu chi').</param>
    /// <param name="branchId">Mã chi nhánh phát sinh chứng từ (tùy chọn).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ValidateTaxPeriodUnlockedAsync(
        DateTime dateUtc,
        string actionDescription,
        Guid? branchId = null,
        CancellationToken cancellationToken = default);
}
