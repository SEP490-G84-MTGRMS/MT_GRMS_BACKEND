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
/// Phương pháp tính thuế hộ kinh doanh theo Luật thuế TNCN, NĐ 68/2026/NĐ-CP và NĐ 141/2026/NĐ-CP.
/// </summary>
public enum TaxCalculationMethod
{
    /// <summary>
    /// Miễn thuế (Doanh thu năm <= 1 tỷ VNĐ).
    /// </summary>
    Exempt = 0,

    /// <summary>
    /// Phương pháp tỷ lệ % trên doanh thu (Doanh thu vượt 1 tỷ).
    /// </summary>
    RevenuePercentage = 1,

    /// <summary>
    /// Phương pháp Kê khai (Thu nhập tính thuế = Doanh thu - Chi phí hợp lý).
    /// </summary>
    DeclarationBased = 2
}

/// <summary>
/// 4 nhóm hộ kinh doanh theo Nghị định 68/2026/NĐ-CP và Nghị định 141/2026/NĐ-CP.
/// </summary>
public enum HouseholdTaxGroup
{
    /// <summary>
    /// Nhóm 1: Doanh thu năm <= 1.000.000.000 VNĐ -> Miễn thuế GTGT & TNCN (0%). Sổ kế toán: S1a-HKD.
    /// </summary>
    Group1_Under1B = 1,

    /// <summary>
    /// Nhóm 2: Doanh thu năm từ 1.000.000.001 đến 3.000.000.000 VNĐ -> Phương pháp tỷ lệ % doanh thu (Bán lẻ: GTGT 1% toàn bộ, TNCN 0.5% phần vượt 1 tỷ). Sổ kế toán: S2a-HKD.
    /// </summary>
    Group2_1BTo3B = 2,

    /// <summary>
    /// Nhóm 3: Doanh thu năm từ 3.000.000.001 đến 50.000.000.000 VNĐ -> Tỷ lệ % doanh thu hoặc Kê khai. Bắt buộc S3a-HKD.
    /// </summary>
    Group3_3BTo50B = 3,

    /// <summary>
    /// Nhóm 4: Doanh thu năm > 50.000.000.000 VNĐ -> Bắt buộc phương pháp Kê khai (Doanh thu - Chi phí).
    /// </summary>
    Group4_Over50B = 4
}

/// <summary>
/// Danh mục 7 mẫu sổ kế toán hộ kinh doanh theo Thông tư 152/2025/TT-BTC (hiệu lực từ 01/01/2026, thay thế TT 88/2021).
/// </summary>
public enum AccountingBookType
{
    /// <summary>
    /// Mẫu S1a-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ (dành cho Nhóm 1).
    /// </summary>
    S1a_RevenueGroup1 = 1,

    /// <summary>
    /// Mẫu S2a-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ theo tỷ lệ % (dành cho Nhóm 2).
    /// </summary>
    S2a_RevenueGroup2 = 2,

    /// <summary>
    /// Mẫu S2b-HKD: Sổ chi tiết doanh thu bán hàng hóa, dịch vụ (dành cho hộ phương pháp Kê khai).
    /// </summary>
    S2b_RevenueDeclaration = 3,

    /// <summary>
    /// Mẫu S2c-HKD: Sổ chi tiết doanh thu, chi phí (dành cho hộ Kê khai để xác định thu nhập tính thuế).
    /// </summary>
    S2c_RevenueAndExpense = 4,

    /// <summary>
    /// Mẫu S2d-HKD: Sổ chi tiết vật liệu, dụng cụ, sản phẩm, hàng hóa (theo dõi nhập xuất tồn).
    /// </summary>
    S2d_MaterialsAndGoods = 5,

    /// <summary>
    /// Mẫu S2e-HKD: Sổ chi tiết tiền (theo dõi thu, chi, tồn quỹ tiền mặt và tiền gửi ngân hàng).
    /// </summary>
    S2e_CashAndBank = 6,

    /// <summary>
    /// Mẫu S3a-HKD: Sổ theo dõi tình hình thực hiện nghĩa vụ thuế với NSNN.
    /// </summary>
    S3a_TaxObligation = 7
}

/// <summary>
/// Các nhóm phân loại thuế hàng hóa theo Nghị quyết 204/2025/QH15 và Luật thuế GTGT.
/// </summary>
public static class TaxCategoryConstants
{
    /// <summary>
    /// Hàng thông thường (thuế suất gốc 1.0%, đủ điều kiện giảm còn 0.8% theo NQ 204).
    /// </summary>
    public const string Standard1Pct = "STANDARD_1PCT";

    /// <summary>
    /// Hàng chịu thuế tiêu thụ đặc biệt hoặc thuộc diện loại trừ (bia, rượu, thuốc lá... giữ nguyên 1.0%, không giảm).
    /// </summary>
    public const string NoReduction1Pct = "NO_REDUCTION_1PCT";

    /// <summary>
    /// Hàng không chịu thuế.
    /// </summary>
    public const string NonTaxable = "NON_TAXABLE";
}