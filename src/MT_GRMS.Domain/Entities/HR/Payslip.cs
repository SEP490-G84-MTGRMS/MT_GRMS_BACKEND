using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng Payslips: Phiếu lương chi tiết của nhân viên theo kỳ tính lương (Hỗ trợ đa chi nhánh & đa hình thức trả lương)
/// </summary>
public class Payslip : BaseTenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }                                  // Chi nhánh tính lương
    public string PayslipCode { get; set; } = string.Empty;             // Mã phiếu lương: PL-YYMM-XXXX
    public string PeriodMonth { get; set; } = string.Empty;             // Kỳ lương: "2026-10"

    // ─── 1. Snapshot Cấu hình lương tại thời điểm chốt công ───
    public SalaryType SalaryType { get; set; } = SalaryType.Monthly;    // Monthly, Hourly, Shift
    public decimal BaseRate { get; set; } = 0.00m;                      // Lương tháng hoặc đơn giá giờ/ca
    public decimal StandardDays { get; set; } = 26.00m;                 // Số ngày công chuẩn của tháng
    public decimal StandardDayHours { get; set; } = 8.00m;              // Giờ chuẩn 1 công (từ BranchAttendancePolicy)

    // ─── 2. Tổng hợp Dữ liệu Chấm công thực tế (từ EmployeeSchedule) ───
    public decimal WorkedDays { get; set; } = 0.00m;                    // Số ngày công làm thực tế (quy đổi)
    public decimal TotalWorkHours { get; set; } = 0.00m;                // Tổng giờ làm việc thực tế
    public int TotalShifts { get; set; } = 0;                           // Tổng số ca đã làm việc
    public decimal OtHours { get; set; } = 0.00m;                       // Tổng số giờ làm thêm (OT)
    public decimal PaidLeaveDays { get; set; } = 0.00m;                 // Số ngày nghỉ phép có lương
    public decimal UnpaidLeaveDays { get; set; } = 0.00m;               // Số ngày nghỉ không lương
    public int TotalLateMinutes { get; set; } = 0;                      // Tổng số phút đi muộn trong tháng
    public int TotalEarlyLeaveMinutes { get; set; } = 0;                // Tổng số phút về sớm trong tháng

    // ─── 3. Thu nhập (Earnings) ───
    public decimal BaseSalaryEarned { get; set; } = 0.00m;              // Lương chính theo công
    public decimal OvertimePay { get; set; } = 0.00m;                   // Tiền làm thêm giờ
    public decimal TotalAllowance { get; set; } = 0.00m;                // Tổng các khoản phụ cấp
    public string? AllowancesDetailJson { get; set; }                   // Snapshot chi tiết các khoản phụ cấp
    public decimal TotalEarnings { get; set; } = 0.00m;                 // Tổng thu nhập trước giảm trừ = BaseSalaryEarned + OvertimePay + TotalAllowance

    // ─── 4. Các khoản giảm trừ (Deductions) ───
    public decimal AdvanceDeduction { get; set; } = 0.00m;              // Khấu trừ tạm ứng lương trong kỳ
    public decimal RemainingDebt { get; set; } = 0.00m;                 // Số tiền tạm ứng còn nợ lại sau kỳ này
    public decimal SocialInsurance { get; set; } = 0.00m;               // Trừ bảo hiểm xã hội (nếu có)
    public decimal TotalDeductions { get; set; } = 0.00m;               // Tổng các khoản giảm trừ = AdvanceDeduction + SocialInsurance

    // ─── 5. Lương thực nhận & Trạng thái ───
    public decimal NetSalary { get; set; } = 0.00m;                     // Lương thực nhận = TotalEarnings - TotalDeductions
    public PayslipStatus Status { get; set; } = PayslipStatus.Unpaid;   // Unpaid, Paid, Cancelled

    // ─── 6. Thông tin thanh toán & Bút toán Sổ quỹ ───
    public DateTime? PaidAt { get; set; }
    public string? PaymentMethod { get; set; }                          // CASH, BANK_TRANSFER, VIETQR
    public string? PaidBy { get; set; }
    public string? BankAccountNumber { get; set; }                      // Snapshot tài khoản nhận
    public string? BankName { get; set; }
    public string? BankAccountHolder { get; set; }
    public Guid? CashbookTransactionId { get; set; }                    // Bút toán Phiếu chi tương ứng
    public string? CashbookVoucherCode { get; set; }                    // Mã phiếu chi: PC-xxxxxx

    // ─── 7. Quy trình phê duyệt & Ghi chú ───
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Note { get; set; }

    // ─── Navigations ───
    public Employee? Employee { get; set; }
    public Branch? Branch { get; set; }
    public CashbookTransaction? CashbookTransaction { get; set; }
    public ICollection<EmployeeSchedule> Schedules { get; set; } = new List<EmployeeSchedule>();
}
