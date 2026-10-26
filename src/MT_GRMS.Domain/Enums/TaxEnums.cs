namespace MT_GRMS.Domain.Enums;

/// <summary>
/// Kỳ tính thuế hộ kinh doanh (FE-19).
/// </summary>
public enum TaxPeriodType
{
    Month = 1,
    Quarter = 2,
    Year = 3
}

/// <summary>
/// 4 nhóm hộ kinh doanh theo Luật Quản lý thuế số 108/2025/QH15 và Nghị định 141/2026/NĐ-CP (hiệu lực từ 01/01/2026).
/// </summary>
public enum HouseholdTaxGroup
{
    /// <summary>
    /// Nhóm 1: Doanh thu năm <= 1.000.000.000 VNĐ -> Miễn thuế GTGT & TNCN (0%).
    /// </summary>
    Group1_Under1B = 1,

    /// <summary>
    /// Nhóm 2: Doanh thu năm từ 1.000.000.001 đến 3.000.000.000 VNĐ -> Nộp thuế theo tỷ lệ % doanh thu (Bán lẻ: 1% GTGT, 0.5% TNCN).
    /// </summary>
    Group2_1BTo3B = 2,

    /// <summary>
    /// Nhóm 3: Doanh thu năm từ 3.000.000.001 đến 50.000.000.000 VNĐ -> Tỷ lệ % doanh thu hoặc Kê khai.
    /// </summary>
    Group3_3BTo50B = 3,

    /// <summary>
    /// Nhóm 4: Doanh thu năm > 50.000.000.000 VNĐ -> Bắt buộc phương pháp Kê khai.
    /// </summary>
    Group4_Over50B = 4
}

/// <summary>
/// Mã sổ kế toán hộ kinh doanh theo Thông tư 152/2025/TT-BTC.
/// </summary>
public enum AccountingBookType
{
    /// <summary>
    /// S2b-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ.
    /// </summary>
    S2b_Revenue = 1,

    /// <summary>
    /// S2c-HKD: Sổ chi tiết chi phí tiền lương, tiền công.
    /// </summary>
    S2c_Salary = 2,

    /// <summary>
    /// S2d-HKD: Sổ chi phí vật liệu, dụng cụ, sản phẩm, hàng hóa.
    /// </summary>
    S2d_Supplies = 3,

    /// <summary>
    /// S2e-HKD: Sổ chi phí dịch vụ mua ngoài và chi phí bằng tiền khác.
    /// </summary>
    S2e_Services = 4
}
