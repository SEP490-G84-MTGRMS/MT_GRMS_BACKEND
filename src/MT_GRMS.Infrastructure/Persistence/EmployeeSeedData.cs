using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu hồ sơ nhân viên (Employee Seed Data) cho MT-GRMS:
/// Khởi tạo 12 nhân viên thực tế phủ kín các vị trí công việc trong chuỗi siêu thị bán lẻ:
/// Cửa hàng trưởng, Quản lý ca, Thu ngân trưởng, Thu ngân POS, Kiểm kho, Tiếp thực, Bảo vệ, Nhân viên thời vụ part-time.
/// </summary>
public static class EmployeeSeedData
{
    private class SeedEmployeeItem
    {
        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CitizenId { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; } = "Nam";
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Address { get; set; }
        public DateTime? StartDate { get; set; }
        public string? Note { get; set; }
        public string? AvatarUrl { get; set; }
        public SalaryType SalaryType { get; set; } = SalaryType.Monthly;
        public decimal BaseRate { get; set; } = 0.00m;
        public string? OtWeekday { get; set; } = "150%";
        public string? OtSunday { get; set; } = "200%";
        public string? BankAccountNumber { get; set; }
        public string? BankName { get; set; }
        public string? BankAccountHolder { get; set; }
        public decimal DebtAmount { get; set; } = 0.00m;
        public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
        public string BranchCodePreference { get; set; } = "BR-HCM-01";
        public string? PreferredUserId { get; set; }
        public List<(string Name, decimal Amount)> Allowances { get; set; } = new();
    }

    public static async Task SeedAsync(AppDbContext context, string tenantId = "tenant_mt_mart_01")
    {
        // 1. Kiểm tra và lấy danh sách chi nhánh của Tenant
        var branches = await context.Branches
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && !b.IsDeleted)
            .ToListAsync();

        if (branches.Count == 0)
        {
            // Fallback: Lấy bất kỳ chi nhánh nào đang có trong DB nếu tenant chưa có
            branches = await context.Branches.IgnoreQueryFilters().Where(b => !b.IsDeleted).ToListAsync();
            if (branches.Count == 0)
            {
                return; // Chưa có chi nhánh nào để gán nhân viên
            }
        }

        var defaultBranch = branches.FirstOrDefault(b => b.BranchCode == "BR-HCM-01") ?? branches[0];
        var hnBranch1 = branches.FirstOrDefault(b => b.BranchCode == "BR-HN-01") ?? defaultBranch;
        var hnBranch2 = branches.FirstOrDefault(b => b.BranchCode == "BR-HN-02") ?? defaultBranch;

        // 2. Danh sách 12 nhân viên mẫu thực tế
        var templates = new List<SeedEmployeeItem>
        {
            // --- Chi nhánh Quận 1 (TP.HCM) ---
            new()
            {
                EmployeeCode = "NV001",
                FullName = "Nguyễn Văn An",
                CitizenId = "079095001234",
                DateOfBirth = new DateTime(1995, 5, 20),
                Gender = "Nam",
                Phone = "0903123456",
                Email = "an.nguyen@mtgrms.vn",
                Address = "123 Hai Bà Trưng, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh",
                StartDate = new DateTime(2022, 1, 15),
                Note = "Cửa hàng trưởng - Quản lý chung vận hành chi nhánh Quận 1",
                SalaryType = SalaryType.Monthly,
                BaseRate = 16000000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "1903456789012",
                BankName = "Techcombank",
                BankAccountHolder = "NGUYEN VAN AN",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                PreferredUserId = "usr_store_manager",
                Allowances = new()
                {
                    ("Phụ cấp trách nhiệm", 2000000.00m),
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp xăng xe", 500000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV002",
                FullName = "Trần Thị Thu Bích",
                CitizenId = "079098005678",
                DateOfBirth = new DateTime(1998, 10, 12),
                Gender = "Nữ",
                Phone = "0903987654",
                Email = "bich.tran@mtgrms.vn",
                Address = "45/2 Nguyễn Thị Minh Khai, Phường Đa Kao, Quận 1, TP. Hồ Chí Minh",
                StartDate = new DateTime(2022, 3, 1),
                Note = "Thu ngân trưởng ca sáng và trưa",
                SalaryType = SalaryType.Monthly,
                BaseRate = 9500000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "0071001234567",
                BankName = "Vietcombank",
                BankAccountHolder = "TRAN THI THU BICH",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                PreferredUserId = "usr_cashier",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp trách nhiệm quầy thu ngân", 600000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV003",
                FullName = "Lê Hoàng Hà",
                CitizenId = "001196024891",
                DateOfBirth = new DateTime(1996, 8, 15),
                Gender = "Nữ",
                Phone = "0932145698",
                Email = "ha.le@mtgrms.vn",
                Address = "78 Trần Đình Xu, Phường Cô Giang, Quận 1, TP. Hồ Chí Minh",
                StartDate = new DateTime(2023, 6, 10),
                Note = "Thu ngân quầy POS ca chiều tối",
                SalaryType = SalaryType.Monthly,
                BaseRate = 8000000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "1903456789013",
                BankName = "Techcombank",
                BankAccountHolder = "LE HOANG HA",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp đi lại", 300000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV004",
                FullName = "Phạm Quốc Hùng",
                CitizenId = "079092003456",
                DateOfBirth = new DateTime(1992, 3, 25),
                Gender = "Nam",
                Phone = "0912345678",
                Email = "hung.pham@mtgrms.vn",
                Address = "102 Lê Lai, Phường Bến Thành, Quận 1, TP. Hồ Chí Minh",
                StartDate = new DateTime(2022, 5, 20),
                Note = "Phụ trách kho tổng, nhập xuất hàng đông lạnh và tươi sống",
                SalaryType = SalaryType.Monthly,
                BaseRate = 9000000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "0912345678999",
                BankName = "MB Bank",
                BankAccountHolder = "PHAM QUOC HUNG",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp độc hại & nâng bốc hàng", 1000000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV011",
                FullName = "Phan Thanh Tùng",
                CitizenId = "079088019876",
                DateOfBirth = new DateTime(1988, 6, 30),
                Gender = "Nam",
                Phone = "0908123987",
                Email = "tung.phan@mtgrms.vn",
                Address = "56 Cống Quỳnh, Phường Phạm Ngũ Lão, Quận 1, TP. Hồ Chí Minh",
                StartDate = new DateTime(2021, 11, 1),
                Note = "Bảo vệ an ninh cửa hàng và trông xe ca tối",
                SalaryType = SalaryType.Monthly,
                BaseRate = 7500000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "6420205123456",
                BankName = "Agribank",
                BankAccountHolder = "PHAN THANH TUNG",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp trực đêm", 800000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV012",
                FullName = "Dương Thị Mai",
                CitizenId = "079093021098",
                DateOfBirth = new DateTime(1993, 2, 14),
                Gender = "Nữ",
                Phone = "0918765432",
                Email = "mai.duong@mtgrms.vn",
                Address = "89 Nguyễn Trãi, Phường Bến Thành, Quận 1, TP. Hồ Chí Minh",
                StartDate = new DateTime(2022, 2, 10),
                Note = "Nhân viên đã bàn giao công việc và nghỉ việc từ tháng 08/2026",
                SalaryType = SalaryType.Monthly,
                BaseRate = 7200000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "060123456789",
                BankName = "Sacombank",
                BankAccountHolder = "DUONG THI MAI",
                Status = EmployeeStatus.Inactive, // Đã thôi việc
                BranchCodePreference = "BR-HCM-01",
                Allowances = new()
            },

            // --- Chi nhánh Cầu Giấy (Hà Nội) ---
            new()
            {
                EmployeeCode = "NV005",
                FullName = "Hoàng Hải Nam",
                CitizenId = "001198031254",
                DateOfBirth = new DateTime(1998, 11, 20),
                Gender = "Nam",
                Phone = "0865731198",
                Email = "nam.hoang@mtgrms.vn",
                Address = "12 Hoàng Quốc Việt, Phường Nghĩa Đô, Quận Cầu Giấy, Hà Nội",
                StartDate = new DateTime(2022, 8, 1),
                Note = "Quản lý điều hành toàn diện chi nhánh Cầu Giấy",
                SalaryType = SalaryType.Monthly,
                BaseRate = 14500000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "0341000654321",
                BankName = "Vietcombank",
                BankAccountHolder = "HOANG HAI NAM",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HN-01",
                Allowances = new()
                {
                    ("Phụ cấp trách nhiệm", 1500000.00m),
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp điện thoại", 400000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV006",
                FullName = "Đỗ Mai Linh",
                CitizenId = "001199045678",
                DateOfBirth = new DateTime(1999, 4, 18),
                Gender = "Nữ",
                Phone = "0987654321",
                Email = "linh.do@mtgrms.vn",
                Address = "45 Trần Thái Tông, Phường Dịch Vọng Hậu, Quận Cầu Giấy, Hà Nội",
                StartDate = new DateTime(2023, 2, 15),
                Note = "Thu ngân và hỗ trợ trưng bày kệ hàng tiêu dùng",
                SalaryType = SalaryType.Monthly,
                BaseRate = 8200000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "234567891",
                BankName = "ACB",
                BankAccountHolder = "DO MAI LINH",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HN-01",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV007",
                FullName = "Vũ Minh Tuấn",
                CitizenId = "001197056789",
                DateOfBirth = new DateTime(1997, 7, 8),
                Gender = "Nam",
                Phone = "0903276611",
                Email = "tuan.vu@mtgrms.vn",
                Address = "88 Cầu Giấy, Phường Quan Hoa, Quận Cầu Giấy, Hà Nội",
                StartDate = new DateTime(2022, 10, 5),
                Note = "Sơ chế và đóng gói thực phẩm tươi sống",
                SalaryType = SalaryType.Monthly,
                BaseRate = 8500000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "12010001234567",
                BankName = "BIDV",
                BankAccountHolder = "VU MINH TUAN",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HN-01",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp bảo quản lạnh", 600000.00m)
                }
            },

            // --- Chi nhánh Thanh Xuân (Hà Nội) ---
            new()
            {
                EmployeeCode = "NV008",
                FullName = "Bùi Thu Trang",
                CitizenId = "001194067890",
                DateOfBirth = new DateTime(1994, 12, 5),
                Gender = "Nữ",
                Phone = "0976543210",
                Email = "trang.bui@mtgrms.vn",
                Address = "15 Nguyễn Trãi, Phường Thượng Đình, Quận Thanh Xuân, Hà Nội",
                StartDate = new DateTime(2022, 4, 1),
                Note = "Cửa hàng phó phụ trách kinh doanh và chăm sóc khách hàng",
                SalaryType = SalaryType.Monthly,
                BaseRate = 12000000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "1902987654321",
                BankName = "Techcombank",
                BankAccountHolder = "BUI THU TRANG",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HN-02",
                Allowances = new()
                {
                    ("Phụ cấp trách nhiệm", 1200000.00m),
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp xăng xe", 500000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV009",
                FullName = "Đặng Quang Huy",
                CitizenId = "001202078901",
                DateOfBirth = new DateTime(2002, 9, 14),
                Gender = "Nam",
                Phone = "0356789012",
                Email = "huy.dang@mtgrms.vn",
                Address = "234 Khuất Duy Tiến, Phường Nhân Chính, Quận Thanh Xuân, Hà Nội",
                StartDate = new DateTime(2024, 1, 10),
                Note = "Sinh viên làm thêm part-time theo giờ ca tối",
                SalaryType = SalaryType.Hourly,
                BaseRate = 28000.00m, // 28.000đ/giờ
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "03567890120",
                BankName = "TPBank",
                BankAccountHolder = "DANG QUANG HUY",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HN-02",
                Allowances = new()
                {
                    ("Phụ cấp ca tối", 250000.00m)
                }
            },
            new()
            {
                EmployeeCode = "NV010",
                FullName = "Ngô Bích Phượng",
                CitizenId = "001201089012",
                DateOfBirth = new DateTime(2001, 1, 22),
                Gender = "Nữ",
                Phone = "0367890123",
                Email = "phuong.ngo@mtgrms.vn",
                Address = "67 Lê Văn Lương, Phường Nhân Chính, Quận Thanh Xuân, Hà Nội",
                StartDate = new DateTime(2024, 3, 1),
                Note = "Nhân viên thời vụ ca linh hoạt cuối tuần",
                SalaryType = SalaryType.Shift,
                BaseRate = 250000.00m, // 250.000đ/ca
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "1567890123",
                BankName = "VPBank",
                BankAccountHolder = "NGO BICH PHUONG",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HN-02",
                Allowances = new()
            }
        };

        // 3. Tiến hành kiểm tra và chèn / đồng bộ nhân viên
        foreach (var tpl in templates)
        {
            var existing = await context.Employees
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && (e.EmployeeCode == tpl.EmployeeCode || e.CitizenId == tpl.CitizenId));

            if (existing != null)
            {
                // Đồng bộ cập nhật các trường mới nếu đang để trống
                if (string.IsNullOrEmpty(existing.Address)) existing.Address = tpl.Address;
                if (!existing.StartDate.HasValue) existing.StartDate = tpl.StartDate;
                if (string.IsNullOrEmpty(existing.Note)) existing.Note = tpl.Note;
                if (string.IsNullOrEmpty(existing.OtWeekday)) existing.OtWeekday = tpl.OtWeekday;
                if (string.IsNullOrEmpty(existing.OtSunday)) existing.OtSunday = tpl.OtSunday;
            }
            else
            {
                // Chọn branch phù hợp
                var assignedBranch = tpl.BranchCodePreference switch
                {
                    "BR-HN-01" => hnBranch1,
                    "BR-HN-02" => hnBranch2,
                    _ => defaultBranch
                };

                // Kiểm tra User nếu có
                string? matchedUserId = null;
                if (!string.IsNullOrEmpty(tpl.PreferredUserId))
                {
                    var userExists = await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == tpl.PreferredUserId);
                    if (userExists)
                    {
                        matchedUserId = tpl.PreferredUserId;
                    }
                }

                var employee = new Employee
                {
                    Id = Uuid7.NewGuid(),
                    TenantId = tenantId,
                    BranchId = assignedBranch.Id,
                    UserId = matchedUserId,
                    EmployeeCode = tpl.EmployeeCode,
                    FullName = tpl.FullName,
                    CitizenId = tpl.CitizenId,
                    DateOfBirth = tpl.DateOfBirth,
                    Gender = tpl.Gender,
                    Phone = tpl.Phone,
                    Email = tpl.Email,
                    Address = tpl.Address,
                    StartDate = tpl.StartDate,
                    Note = tpl.Note,
                    OtWeekday = tpl.OtWeekday,
                    OtSunday = tpl.OtSunday,
                    SalaryType = tpl.SalaryType,
                    BaseRate = tpl.BaseRate,
                    BankAccountNumber = tpl.BankAccountNumber,
                    BankName = tpl.BankName,
                    BankAccountHolder = tpl.BankAccountHolder,
                    DebtAmount = tpl.DebtAmount,
                    Status = tpl.Status,
                    CreatedAt = DateTime.UtcNow
                };

                foreach (var (name, amount) in tpl.Allowances)
                {
                    employee.Allowances.Add(new EmployeeAllowance
                    {
                        Id = Uuid7.NewGuid(),
                        TenantId = tenantId,
                        EmployeeId = employee.Id,
                        Name = name,
                        Amount = amount,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await context.Employees.AddAsync(employee);
            }
        }

        await context.SaveChangesAsync();
    }
}
