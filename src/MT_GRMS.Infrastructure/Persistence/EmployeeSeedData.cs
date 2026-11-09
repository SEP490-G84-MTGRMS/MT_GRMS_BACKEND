using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu mẫu hồ sơ nhân viên, cấu hình lương, chính sách chấm công, ca làm việc,
/// phân ca và bảng lương (Payslip) cho hệ thống quản lý bán lẻ MT-GRMS.
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
        public string? FaceDescriptor { get; set; }
        public string? FaceImageUrls { get; set; }
        public DateTime? FaceRegisteredAt { get; set; }
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

    /// <summary>
    /// Tạo chuỗi JSON mảng vector 128 số float mẫu theo chuẩn face-api.js để seed dữ liệu FaceID
    /// </summary>
    private static string GenerateSampleFaceDescriptorJson(int seedOffset = 0, int sampleCount = 2)
    {
        var descriptors = new List<float[]>();
        for (int s = 0; s < sampleCount; s++)
        {
            var vector = new float[128];
            for (int i = 0; i < 128; i++)
            {
                vector[i] = (float)Math.Round(Math.Sin((i + seedOffset + s * 10) * 0.15) * 0.15, 6);
            }
            descriptors.Add(vector);
        }
        return JsonSerializer.Serialize(descriptors);
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
            branches = await context.Branches.IgnoreQueryFilters().Where(b => !b.IsDeleted).ToListAsync();
            if (branches.Count == 0)
            {
                return;
            }
        }

        var defaultBranch = branches.FirstOrDefault(b => b.BranchCode == "BR-HCM-01") ?? branches[0];
        var hnBranch1 = branches.FirstOrDefault(b => b.BranchCode == "BR-HN-01") ?? defaultBranch;
        var hnBranch2 = branches.FirstOrDefault(b => b.BranchCode == "BR-HN-02") ?? defaultBranch;

        // 2. YÊU CẦU 1: Khởi tạo Chính sách Chấm công Chi nhánh (BranchAttendancePolicy)
        await SeedBranchAttendancePoliciesAsync(context, tenantId, branches);

        // 3. YÊU CẦU 3: Khởi tạo Danh mục Ca làm việc chuẩn 8 tiếng (ShiftConfig)
        await SeedShiftConfigsAsync(context, tenantId, branches);

        // 4. YÊU CẦU 2: Danh sách nhân viên (Mô hình tinh gọn: Toàn bộ hệ thống chỉ có Chủ cửa hàng + Nhân viên gộp chung)
        var templates = new List<SeedEmployeeItem>
        {
            // --- Chi nhánh Quận 1 (BR-HCM-01): 3 Nhân viên với 3 cơ chế lương khác nhau ---
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
                Note = "Nhân viên (Lương theo tháng - Monthly)",
                AvatarUrl = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 1, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
                SalaryType = SalaryType.Monthly,
                BaseRate = 10000000.00m, // 10.000.000 VNĐ/tháng
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "1903456789012",
                BankName = "Techcombank",
                BankAccountHolder = "NGUYEN VAN AN",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                PreferredUserId = "usr_staff",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m),
                    ("Phụ cấp trách nhiệm", 1000000.00m),
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
                Note = "Nhân viên (Lương theo ca - Shift)",
                AvatarUrl = "https://images.unsplash.com/photo-1494790108377-be9c29b29330?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 2, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1494790108377-be9c29b29330?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
                SalaryType = SalaryType.Shift,
                BaseRate = 150000.00m, // 150.000 VNĐ/ca
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "0071001234567",
                BankName = "Vietcombank",
                BankAccountHolder = "TRAN THI THU BICH",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 730000.00m)
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
                Note = "Nhân viên (Lương theo giờ - Hourly)",
                AvatarUrl = "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 3, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1438761681033-6461ffad8d80?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
                SalaryType = SalaryType.Hourly,
                BaseRate = 25000.00m, // 25.000 VNĐ/giờ
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "1903456789013",
                BankName = "MB Bank",
                BankAccountHolder = "LE HOANG HA",
                Status = EmployeeStatus.Active,
                BranchCodePreference = "BR-HCM-01",
                Allowances = new()
                {
                    ("Phụ cấp ăn trưa", 500000.00m)
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
                Note = "Nhân viên (Lương theo tháng - Monthly)",
                AvatarUrl = "https://images.unsplash.com/photo-1570295999919-56ceb5ecca61?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 4, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1570295999919-56ceb5ecca61?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
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
                Note = "Nhân viên (Lương theo tháng - Monthly)",
                AvatarUrl = "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 11, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
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
                Note = "Nhân viên đã nghỉ việc từ tháng 08/2026",
                AvatarUrl = "https://images.unsplash.com/photo-1524504388940-b1c1722653e1?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 12, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1524504388940-b1c1722653e1?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
                SalaryType = SalaryType.Monthly,
                BaseRate = 7200000.00m,
                OtWeekday = "150%",
                OtSunday = "200%",
                BankAccountNumber = "060123456789",
                BankName = "Sacombank",
                BankAccountHolder = "DUONG THI MAI",
                Status = EmployeeStatus.Inactive,
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
                Note = "Nhân viên (Lương theo tháng - Monthly)",
                AvatarUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 5, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
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
                    ("Phụ cấp ăn trưa", 730000.00m)
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
                Note = "Nhân viên (Lương theo tháng - Monthly)",
                AvatarUrl = "https://images.unsplash.com/photo-1544005313-94ddf0286df2?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 6, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1544005313-94ddf0286df2?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
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
                Note = "Nhân viên (Lương theo tháng - Monthly)",
                AvatarUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 7, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
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
                Note = "Nhân viên (Lương theo tháng - Monthly)",
                AvatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 8, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
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
                    ("Phụ cấp ăn trưa", 730000.00m)
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
                Note = "Nhân viên (Lương theo giờ - Hourly)",
                AvatarUrl = "https://images.unsplash.com/photo-1519085360753-af0119f7cbe7?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 9, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1519085360753-af0119f7cbe7?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
                SalaryType = SalaryType.Hourly,
                BaseRate = 28000.00m,
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
                Note = "Nhân viên (Lương theo ca - Shift)",
                AvatarUrl = "https://images.unsplash.com/photo-1517841905240-472988babdf9?auto=format&fit=crop&w=300&q=80",
                FaceDescriptor = GenerateSampleFaceDescriptorJson(seedOffset: 10, sampleCount: 2),
                FaceImageUrls = JsonSerializer.Serialize(new[] { "https://images.unsplash.com/photo-1517841905240-472988babdf9?auto=format&fit=crop&w=300&q=80" }),
                FaceRegisteredAt = DateTime.UtcNow.AddMonths(-2),
                SalaryType = SalaryType.Shift,
                BaseRate = 250000.00m,
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

        // 5. Kiểm tra và chèn / đồng bộ hồ sơ nhân viên (Kiểm tra Exist không thì mới add)
        foreach (var tpl in templates)
        {
            var existing = await context.Employees
                .Include(e => e.Allowances)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && (e.EmployeeCode == tpl.EmployeeCode || e.CitizenId == tpl.CitizenId));

            if (existing != null)
            {
                if (string.IsNullOrEmpty(existing.Address)) existing.Address = tpl.Address;
                if (!existing.StartDate.HasValue) existing.StartDate = tpl.StartDate;
                if (string.IsNullOrEmpty(existing.Note)) existing.Note = tpl.Note;
                if (string.IsNullOrEmpty(existing.OtWeekday)) existing.OtWeekday = tpl.OtWeekday;
                if (string.IsNullOrEmpty(existing.OtSunday)) existing.OtSunday = tpl.OtSunday;
                if (string.IsNullOrEmpty(existing.BankAccountNumber)) existing.BankAccountNumber = tpl.BankAccountNumber;
                if (string.IsNullOrEmpty(existing.BankName)) existing.BankName = tpl.BankName;
                if (string.IsNullOrEmpty(existing.BankAccountHolder)) existing.BankAccountHolder = tpl.BankAccountHolder;
                if (string.IsNullOrEmpty(existing.AvatarUrl) && !string.IsNullOrEmpty(tpl.AvatarUrl)) existing.AvatarUrl = tpl.AvatarUrl;
                if (string.IsNullOrEmpty(existing.FaceDescriptor) && !string.IsNullOrEmpty(tpl.FaceDescriptor))
                {
                    existing.FaceDescriptor = tpl.FaceDescriptor;
                    existing.FaceRegisteredAt = tpl.FaceRegisteredAt ?? DateTime.UtcNow;
                }
                if (string.IsNullOrEmpty(existing.FaceImageUrls) && !string.IsNullOrEmpty(tpl.FaceImageUrls)) existing.FaceImageUrls = tpl.FaceImageUrls;

                // Đồng bộ phụ cấp: Kiểm tra từng phụ cấp trong template, nếu chưa Exist thì mới add
                foreach (var (name, amount) in tpl.Allowances)
                {
                    if (!existing.Allowances.Any(a => a.Name == name))
                    {
                        // Thêm qua DbSet (state Added) - tránh EF coi entity có sẵn Id là Modified → UPDATE 0 dòng
                        context.EmployeeAllowances.Add(new EmployeeAllowance
                        {
                            Id = Uuid7.NewGuid(),
                            TenantId = tenantId,
                            EmployeeId = existing.Id,
                            Name = name,
                            Amount = amount,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                // Đồng bộ cấu hình lương cho NV001, NV002, NV003 chuẩn theo yêu cầu đề bài
                if (tpl.EmployeeCode is "NV001" or "NV002" or "NV003")
                {
                    existing.SalaryType = tpl.SalaryType;
                    existing.BaseRate = tpl.BaseRate;
                    existing.BranchId = defaultBranch.Id;
                }
            }
            else
            {
                var assignedBranch = tpl.BranchCodePreference switch
                {
                    "BR-HN-01" => hnBranch1,
                    "BR-HN-02" => hnBranch2,
                    _ => defaultBranch
                };

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
                    AvatarUrl = tpl.AvatarUrl,
                    FaceDescriptor = tpl.FaceDescriptor,
                    FaceImageUrls = tpl.FaceImageUrls,
                    FaceRegisteredAt = tpl.FaceRegisteredAt,
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

        // 6. YÊU CẦU 4 & 5: Phân ca, Chấm công giả lập & Tính toán Bảng lương (Payslip) Tháng 9 và Tháng 10 năm 2026
        await SeedSchedulesAndPayslipsAsync(context, tenantId, defaultBranch.Id);
    }

    /// <summary>
    /// YÊU CẦU 1: Khởi tạo chính sách chấm công (BranchAttendancePolicy) cho chi nhánh
    /// </summary>
    public static async Task SeedBranchAttendancePoliciesAsync(
        AppDbContext context,
        string tenantId,
        List<Branch> branches)
    {
        const string defaultConversionRulesJson =
            "[{\"id\":\"rule-1\",\"type\":\"range\",\"fromHours\":1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":30,\"workUnits\":0.5,\"workUnitsLabel\":\"nửa công\"},{\"id\":\"rule-2\",\"type\":\"gte\",\"fromHours\":4,\"fromMinutes\":30,\"workUnits\":1,\"workUnitsLabel\":\"1 công\"}]";

        var hasChanges = false;

        foreach (var branch in branches)
        {
            var existingPolicy = await context.BranchAttendancePolicies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.BranchId == branch.Id);

            if (existingPolicy == null)
            {
                var policy = new BranchAttendancePolicy
                {
                    Id = Uuid7.NewGuid(),
                    TenantId = tenantId,
                    BranchId = branch.Id,
                    StandardDayHours = 8.00m,
                    IsConvertHoursToDays = true,
                    ConversionRulesJson = defaultConversionRulesJson,
                    EnableLateThreshold = true,
                    LateThresholdMinutes = 15, // Đi muộn sau 15 phút
                    EnableEarlyLeaveThreshold = true,
                    EarlyLeaveThresholdMinutes = 15, // Về sớm trước 15 phút
                    CreatedAt = DateTime.UtcNow
                };

                await context.BranchAttendancePolicies.AddAsync(policy);
                hasChanges = true;
            }
            else
            {
                // Đồng bộ các thông số chuẩn nếu chưa khớp
                existingPolicy.StandardDayHours = 8.00m;
                existingPolicy.IsConvertHoursToDays = true;
                existingPolicy.EnableLateThreshold = true;
                existingPolicy.LateThresholdMinutes = 15;
                existingPolicy.EnableEarlyLeaveThreshold = true;
                existingPolicy.EarlyLeaveThresholdMinutes = 15;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// YÊU CẦU 3: Khởi tạo danh mục Ca làm việc chuẩn 8 tiếng (ShiftConfig) phù hợp với mô hình siêu thị/tạp hóa
    /// - Ca sáng: 6h - 14h
    /// - Ca chiều: 14h - 22h
    /// - Ca đêm: 22h - 6h (kết thúc hôm sau)
    /// </summary>
    public static async Task SeedShiftConfigsAsync(
        AppDbContext context,
        string tenantId,
        List<Branch> branches)
    {
        var defaultBranch = branches.FirstOrDefault(b => b.BranchCode == "BR-HCM-01") ?? branches[0];
        var allBranchIds = branches.Select(b => b.Id.ToString()).ToList();
        var allBranchIdsJson = JsonSerializer.Serialize(allBranchIds);

        var shiftTemplates = new[]
        {
            new
            {
                ShiftCode = "CA-SANG",
                ShiftName = "Ca sáng (6h - 14h)",
                StartTime = new TimeSpan(6, 0, 0),
                EndTime = new TimeSpan(14, 0, 0),
                DurationHours = 8.00m,
                AllowedCheckInStart = new TimeSpan(5, 30, 0),
                AllowedCheckInEnd = new TimeSpan(7, 0, 0),
                IsCheckInNextDay = false,
                Description = "Ca sáng: Tiếp nhận thực phẩm tươi sống, trưng bày quầy kệ & phục vụ bán hàng",
                IsActive = true
            },
            new
            {
                ShiftCode = "CA-CHIEU",
                ShiftName = "Ca chiều (14h - 22h)",
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(22, 0, 0),
                DurationHours = 8.00m,
                AllowedCheckInStart = new TimeSpan(13, 30, 0),
                AllowedCheckInEnd = new TimeSpan(15, 0, 0),
                IsCheckInNextDay = false,
                Description = "Ca chiều: Phục vụ khách hàng giờ cao điểm tối & kết ca kiểm đếm quỹ tiền mặt",
                IsActive = true
            },
            new
            {
                ShiftCode = "CA-DEM",
                ShiftName = "Ca đêm (22h - 6h)",
                StartTime = new TimeSpan(22, 0, 0),
                EndTime = new TimeSpan(6, 0, 0),
                DurationHours = 8.00m,
                AllowedCheckInStart = new TimeSpan(21, 30, 0),
                AllowedCheckInEnd = new TimeSpan(23, 0, 0),
                IsCheckInNextDay = true,
                Description = "Ca trực đêm: Kiểm kê hàng hóa định kỳ, vệ sinh quầy kệ & nhập kho hàng lạnh",
                IsActive = true
            },
            new
            {
                ShiftCode = "CA-HANHCHINH",
                ShiftName = "Ca hành chính",
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                DurationHours = 8.00m,
                AllowedCheckInStart = new TimeSpan(7, 30, 0),
                AllowedCheckInEnd = new TimeSpan(9, 0, 0),
                IsCheckInNextDay = false,
                Description = "Ca làm việc hành chính: Quản trị chi nhánh, kế toán kho & nhân sự",
                IsActive = true
            },
            new
            {
                ShiftCode = "CA-TOI",
                ShiftName = "Ca tối part-time",
                StartTime = new TimeSpan(18, 0, 0),
                EndTime = new TimeSpan(22, 0, 0),
                DurationHours = 4.00m,
                AllowedCheckInStart = new TimeSpan(17, 30, 0),
                AllowedCheckInEnd = new TimeSpan(18, 30, 0),
                IsCheckInNextDay = false,
                Description = "Ca tối 4 tiếng: Tăng cường thu ngân & tiếp thực phục vụ giờ cao điểm",
                IsActive = true
            }
        };

        var hasChanges = false;

        foreach (var tpl in shiftTemplates)
        {
            var existing = await context.ShiftConfigs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == defaultBranch.Id && s.ShiftCode == tpl.ShiftCode);

            if (existing == null)
            {
                var shift = new ShiftConfig
                {
                    Id = Uuid7.NewGuid(),
                    TenantId = tenantId,
                    BranchId = defaultBranch.Id,
                    BranchIds = allBranchIdsJson,
                    ShiftCode = tpl.ShiftCode,
                    ShiftName = tpl.ShiftName,
                    StartTime = tpl.StartTime,
                    EndTime = tpl.EndTime,
                    DurationHours = tpl.DurationHours,
                    AllowedCheckInStart = tpl.AllowedCheckInStart,
                    AllowedCheckInEnd = tpl.AllowedCheckInEnd,
                    IsCheckInNextDay = tpl.IsCheckInNextDay,
                    Description = tpl.Description,
                    IsActive = tpl.IsActive,
                    CreatedAt = DateTime.UtcNow
                };

                await context.ShiftConfigs.AddAsync(shift);
                hasChanges = true;
            }
            else
            {
                // Đồng bộ giờ ca chuẩn 8 tiếng
                existing.StartTime = tpl.StartTime;
                existing.EndTime = tpl.EndTime;
                existing.DurationHours = tpl.DurationHours;
                existing.IsCheckInNextDay = tpl.IsCheckInNextDay;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// YÊU CẦU 4 & 5:
    /// - Quét qua 61 ngày (01/09/2026 - 31/10/2026) tạo EmployeeSchedule giả lập chấm công (đi muộn, về sớm, OT)
    /// - Tính toán động tạo Payslip tháng 9 và tháng 10 dựa trên ConfigPolicy và dữ liệu chấm công thực tế
    /// </summary>
    public static async Task SeedSchedulesAndPayslipsAsync(
        AppDbContext context,
        string tenantId,
        Guid branchId)
    {
        // 1. Lấy thông tin 3 nhân viên trọng tâm tại chi nhánh
        var targetEmployees = await context.Employees
            .Include(e => e.Allowances)
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.BranchId == branchId &&
                        (e.EmployeeCode == "NV001" || e.EmployeeCode == "NV002" || e.EmployeeCode == "NV003"))
            .ToListAsync();

        if (targetEmployees.Count < 3)
        {
            return;
        }

        var empMonthly = targetEmployees.First(e => e.EmployeeCode == "NV001"); // Lương tháng 10.000.000
        var empShift = targetEmployees.First(e => e.EmployeeCode == "NV002");   // Lương ca 150.000
        var empHourly = targetEmployees.First(e => e.EmployeeCode == "NV003");  // Lương giờ 25.000

        // 2. Lấy Policy chấm công của chi nhánh
        var policy = await context.BranchAttendancePolicies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.BranchId == branchId);

        if (policy == null)
        {
            return;
        }

        // 3. Lấy các Ca làm việc chuẩn 8 tiếng
        var shifts = await context.ShiftConfigs
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.BranchId == branchId)
            .ToListAsync();

        var shiftMorning = shifts.FirstOrDefault(s => s.ShiftCode == "CA-SANG") ?? shifts[0];
        var shiftAfternoon = shifts.FirstOrDefault(s => s.ShiftCode == "CA-CHIEU") ?? shifts[0];

        // 4. Kiểm tra xem đã seed lịch chấm công cho kỳ 01/09/2026 - 31/10/2026 chưa (Kiểm tra Exist từng ca)
        var startDate = new DateTime(2026, 9, 1);
        var endDate = new DateTime(2026, 10, 31);

        var existingScheduleKeys = await context.EmployeeSchedules
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.BranchId == branchId &&
                        s.WorkDate >= startDate && s.WorkDate <= endDate &&
                        (s.EmployeeId == empMonthly.Id || s.EmployeeId == empShift.Id || s.EmployeeId == empHourly.Id))
            .Select(s => new { s.EmployeeId, s.WorkDate, s.ShiftConfigId })
            .ToListAsync();

        var existingSet = new HashSet<string>(
            existingScheduleKeys.Select(k => $"{k.EmployeeId}_{k.WorkDate:yyyyMMdd}_{k.ShiftConfigId}"));

        List<EmployeeSchedule> newSchedules = new();

        // Quét qua 61 ngày (Tháng 9 có 30 ngày, Tháng 10 có 31 ngày)
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var isSunday = date.DayOfWeek == DayOfWeek.Sunday;

            // --- Nhân viên 1: NV001 (Monthly) - Làm Ca sáng (6h - 14h), Nghỉ Chủ Nhật ---
            if (!isSunday)
            {
                var key = $"{empMonthly.Id}_{date:yyyyMMdd}_{shiftMorning.Id}";
                if (!existingSet.Contains(key))
                {
                    var (lateMin, earlyMin, otMin, actualHours, checkIn, checkOut) =
                        SimulateAttendance(date, shiftMorning, date.Day switch
                        {
                            5 or 24 => AttendanceScenario.Late20Min,      // Đi muộn trễ 20 phút (vượt ngưỡng 15p)
                            15 or 28 => AttendanceScenario.EarlyLeave20Min, // Về sớm 20 phút (vượt ngưỡng 15p)
                            10 or 22 => AttendanceScenario.Overtime45Min,  // Làm thêm giờ 45 phút (vượt ngưỡng 30p)
                            _ => AttendanceScenario.Normal
                        });

                    newSchedules.Add(CreateSchedule(tenantId, branchId, empMonthly.Id, shiftMorning, date,
                        checkIn, checkOut, lateMin, earlyMin, otMin, actualHours));
                    existingSet.Add(key);
                }
            }

            // --- Nhân viên 2: NV002 (Shift) - Làm Ca chiều (14h - 22h), Nghỉ Chủ Nhật ---
            if (!isSunday)
            {
                var key = $"{empShift.Id}_{date:yyyyMMdd}_{shiftAfternoon.Id}";
                if (!existingSet.Contains(key))
                {
                    var (lateMin, earlyMin, otMin, actualHours, checkIn, checkOut) =
                        SimulateAttendance(date, shiftAfternoon, date.Day switch
                        {
                            8 or 19 => AttendanceScenario.Late20Min,       // Đi muộn trễ 20 phút
                            12 or 27 => AttendanceScenario.EarlyLeave20Min, // Về sớm 20 phút
                            16 or 25 => AttendanceScenario.Overtime45Min,  // Làm thêm giờ 45 phút
                            _ => AttendanceScenario.Normal
                        });

                    newSchedules.Add(CreateSchedule(tenantId, branchId, empShift.Id, shiftAfternoon, date,
                        checkIn, checkOut, lateMin, earlyMin, otMin, actualHours));
                    existingSet.Add(key);
                }
            }

            // --- Nhân viên 3: NV003 (Hourly) - Làm Ca sáng từ Thứ 2 - Thứ 6 + một vài ca thêm Chủ Nhật/Thứ 7 ---
            var isSaturday = date.DayOfWeek == DayOfWeek.Saturday;
            var shouldWorkHourly = (!isSunday && !isSaturday) || (isSaturday && date.Day % 2 == 0);

            if (shouldWorkHourly)
            {
                var key = $"{empHourly.Id}_{date:yyyyMMdd}_{shiftMorning.Id}";
                if (!existingSet.Contains(key))
                {
                    var (lateMin, earlyMin, otMin, actualHours, checkIn, checkOut) =
                        SimulateAttendance(date, shiftMorning, date.Day switch
                        {
                            4 or 18 => AttendanceScenario.Late20Min,       // Đi muộn trễ 20 phút
                            11 or 29 => AttendanceScenario.EarlyLeave20Min, // Về sớm 20 phút
                            7 or 21 => AttendanceScenario.Overtime45Min,   // Làm thêm giờ 45 phút
                            _ => AttendanceScenario.Normal
                        });

                    newSchedules.Add(CreateSchedule(tenantId, branchId, empHourly.Id, shiftMorning, date,
                        checkIn, checkOut, lateMin, earlyMin, otMin, actualHours));
                    existingSet.Add(key);
                }
            }
        }

        if (newSchedules.Count > 0)
        {
            await context.EmployeeSchedules.AddRangeAsync(newSchedules);
            await context.SaveChangesAsync();
        }

        var allSchedules = await context.EmployeeSchedules
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.BranchId == branchId &&
                        s.WorkDate >= startDate && s.WorkDate <= endDate &&
                        (s.EmployeeId == empMonthly.Id || s.EmployeeId == empShift.Id || s.EmployeeId == empHourly.Id))
            .ToListAsync();

        // 5. YÊU CẦU 5: Tính toán Payslip cho Tháng 9 và Tháng 10 áp dụng ConfigPolicy
        // Kiểm tra xem đã có Payslip của kỳ này chưa
        var periods = new[] { "2026-09", "2026-10" };

        foreach (var period in periods)
        {
            var year = int.Parse(period.Split('-')[0]);
            var month = int.Parse(period.Split('-')[1]);
            var periodStartDate = new DateTime(year, month, 1);
            var periodEndDate = periodStartDate.AddMonths(1).AddDays(-1);

            foreach (var emp in targetEmployees)
            {
                var existingPayslip = await context.Payslips
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.BranchId == branchId &&
                                              p.EmployeeId == emp.Id && p.PeriodMonth == period);

                if (existingPayslip != null)
                {
                    continue; // Đã có phiếu lương cho nhân viên trong tháng này
                }

                // Lấy toàn bộ ca làm việc của nhân viên trong tháng
                var monthSchedules = allSchedules
                    .Where(s => s.EmployeeId == emp.Id && s.WorkDate >= periodStartDate && s.WorkDate <= periodEndDate)
                    .ToList();

                if (monthSchedules.Count == 0)
                {
                    continue;
                }

                // ─── THỰC HIỆN TÍNH TOÁN DỰA TRÊN POLICY (Dùng các biến tạm lưu trữ trong vòng lặp) ───
                decimal totalActualHours = 0.00m;
                decimal totalOtHours = 0.00m;
                int totalShiftsCount = 0;
                int totalLateMinutes = 0;
                int totalEarlyLeaveMinutes = 0;
                decimal overtimePay = 0.00m;

                // Đơn giá 1 giờ chuẩn để tính làm thêm giờ (OT)
                decimal hourlyStandardRate = emp.SalaryType switch
                {
                    SalaryType.Monthly => Math.Round(emp.BaseRate / (26m * policy.StandardDayHours), 2),
                    SalaryType.Shift => Math.Round(emp.BaseRate / policy.StandardDayHours, 2),
                    SalaryType.Hourly => emp.BaseRate,
                    _ => 0.00m
                };

                foreach (var sch in monthSchedules)
                {
                    totalActualHours += sch.ActualWorkHours;
                    totalShiftsCount += 1;

                    // Theo dõi số phút Đi muộn theo Policy
                    if (policy.EnableLateThreshold && sch.LateMinutes > policy.LateThresholdMinutes)
                    {
                        totalLateMinutes += sch.LateMinutes;
                    }

                    // Theo dõi số phút Về sớm theo Policy
                    if (policy.EnableEarlyLeaveThreshold && sch.EarlyLeaveMinutes > policy.EarlyLeaveThresholdMinutes)
                    {
                        totalEarlyLeaveMinutes += sch.EarlyLeaveMinutes;
                    }

                    // Tính thời gian làm thêm ngoài giờ (OT) từ số phút thực tế
                    if (sch.OvertimeMinutes > 0)
                    {
                        decimal otHoursInShift = Math.Round((decimal)sch.OvertimeMinutes / 60.00m, 2);
                        totalOtHours += otHoursInShift;

                        // Hệ số OT: Chủ nhật 200%, Ngày thường 150%
                        decimal otMultiplier = sch.WorkDate.DayOfWeek == DayOfWeek.Sunday ? 2.00m : 1.50m;
                        overtimePay += Math.Round(otHoursInShift * hourlyStandardRate * otMultiplier, 2);
                    }
                }

                // ─── TÍNH LƯƠNG CHÍNH (BaseSalaryEarned) & NGÀY CÔNG (WorkedDays) ───
                decimal workedDays = 0.00m;
                decimal baseSalaryEarned = 0.00m;

                if (emp.SalaryType == SalaryType.Monthly)
                {
                    // Với nhân viên lương tháng: Nếu IsConvertHoursToDays = true, quy đổi giờ ra ngày công
                    if (policy.IsConvertHoursToDays && policy.StandardDayHours > 0)
                    {
                        workedDays = Math.Round(totalActualHours / policy.StandardDayHours, 2);
                    }
                    else
                    {
                        workedDays = totalShiftsCount;
                    }

                    // Lương chính theo ngày công thực tế (chuẩn 26 ngày/tháng)
                    baseSalaryEarned = Math.Round((workedDays / 26.00m) * emp.BaseRate, 2);
                }
                else if (emp.SalaryType == SalaryType.Shift)
                {
                    // Với nhân viên lương ca: Tính theo số ca hoàn thành
                    workedDays = totalShiftsCount;
                    baseSalaryEarned = Math.Round(totalShiftsCount * emp.BaseRate, 2);
                }
                else if (emp.SalaryType == SalaryType.Hourly)
                {
                    // Với nhân viên lương giờ: Tính theo tổng giờ làm việc thực tế
                    workedDays = Math.Round(totalActualHours / policy.StandardDayHours, 2);
                    baseSalaryEarned = Math.Round(totalActualHours * emp.BaseRate, 2);
                }

                // ─── PHỤ CẤP ───
                decimal totalAllowance = emp.Allowances.Sum(a => a.Amount);
                string? allowancesJson = emp.Allowances.Any()
                    ? JsonSerializer.Serialize(emp.Allowances.Select(a => new { a.Name, a.Amount }))
                    : null;

                decimal totalEarnings = baseSalaryEarned + overtimePay + totalAllowance;

                // ─── CÁC KHOẢN GIẢM TRỪ ───
                // Trừ BHXH 10.5% đối với nhân viên ký hợp đồng lương tháng chính thức
                decimal socialInsurance = (emp.SalaryType == SalaryType.Monthly) ? Math.Round(emp.BaseRate * 0.105m, 2) : 0.00m;
                decimal advanceDeduction = 0.00m; // Không có tạm ứng trong kỳ
                decimal remainingDebt = emp.DebtAmount;
                decimal totalDeductions = socialInsurance + advanceDeduction;

                // ─── LƯƠNG THỰC NHẬN (NetSalary) ───
                decimal netSalary = totalEarnings - totalDeductions;

                // Sinh mã phiếu lương: PL-YYMM-MãNV (VD: PL-2609-NV001)
                var shortPeriodCode = period.Replace("-", "").Substring(2);
                var payslipCode = $"PL-{shortPeriodCode}-{emp.EmployeeCode}";

                // Tháng 9: Đã thanh toán chuyển khoản vào ngày 05/10/2026
                // Tháng 10: Vừa chốt kỳ, trạng thái Chưa thanh toán (Unpaid)
                var isPaid = period == "2026-09";

                var payslip = new Payslip
                {
                    Id = Uuid7.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = emp.Id,
                    BranchId = branchId,
                    PayslipCode = payslipCode,
                    PeriodMonth = period,

                    // 1. Snapshot Cấu hình lương
                    SalaryType = emp.SalaryType,
                    BaseRate = emp.BaseRate,
                    StandardDays = 26.00m,
                    StandardDayHours = policy.StandardDayHours,

                    // 2. Dữ liệu chấm công thực tế
                    WorkedDays = workedDays,
                    TotalWorkHours = totalActualHours,
                    TotalShifts = totalShiftsCount,
                    OtHours = totalOtHours,
                    PaidLeaveDays = 0.00m,
                    UnpaidLeaveDays = 0.00m,
                    TotalLateMinutes = totalLateMinutes,
                    TotalEarlyLeaveMinutes = totalEarlyLeaveMinutes,

                    // 3. Thu nhập
                    BaseSalaryEarned = baseSalaryEarned,
                    OvertimePay = overtimePay,
                    TotalAllowance = totalAllowance,
                    AllowancesDetailJson = allowancesJson,
                    TotalEarnings = totalEarnings,

                    // 4. Giảm trừ
                    AdvanceDeduction = advanceDeduction,
                    RemainingDebt = remainingDebt,
                    SocialInsurance = socialInsurance,
                    TotalDeductions = totalDeductions,

                    // 5. Thực nhận & Trạng thái
                    NetSalary = netSalary,
                    Status = isPaid ? PayslipStatus.Paid : PayslipStatus.Unpaid,

                    // 6. Thanh toán
                    PaidAt = isPaid ? new DateTime(2026, 10, 5, 9, 30, 0) : null,
                    PaymentMethod = isPaid ? "BANK_TRANSFER" : null,
                    PaidBy = isPaid ? "usr_store_owner" : null,
                    BankAccountNumber = emp.BankAccountNumber,
                    BankName = emp.BankName,
                    BankAccountHolder = emp.BankAccountHolder,

                    // 7. Phê duyệt & ghi chú
                    ApprovedBy = "usr_store_owner",
                    ApprovedAt = periodEndDate.AddDays(1).AddHours(17),
                    Note = isPaid
                        ? $"Phiếu lương kỳ {period} đã quyết toán và chi trả qua Techcombank/Vietcombank"
                        : $"Bảng lương kỳ {period} đã chốt công, đang chờ phê duyệt thanh toán",
                    CreatedAt = DateTime.UtcNow
                };

                await context.Payslips.AddAsync(payslip);

                // Gắn liên kết PayslipId vào các ca làm việc tương ứng trong tháng
                foreach (var sch in monthSchedules)
                {
                    sch.PayslipId = payslip.Id;
                }
            }

            await context.SaveChangesAsync();
        }
    }

    private enum AttendanceScenario
    {
        Normal,
        Late20Min,
        EarlyLeave20Min,
        Overtime45Min
    }

    /// <summary>
    /// Giả lập dữ liệu điểm danh check-in, check-out và tính toán dung sai (muộn, về sớm, OT)
    /// </summary>
    private static (int lateMin, int earlyMin, int otMin, decimal actualHours, DateTime checkIn, DateTime checkOut)
        SimulateAttendance(DateTime workDate, ShiftConfig shift, AttendanceScenario scenario)
    {
        var plannedStart = workDate.Date + shift.StartTime;
        var plannedEnd = shift.IsCheckInNextDay
            ? workDate.Date.AddDays(1) + shift.EndTime
            : workDate.Date + shift.EndTime;

        int lateMin = 0;
        int earlyMin = 0;
        int otMin = 0;
        DateTime checkIn;
        DateTime checkOut;

        switch (scenario)
        {
            case AttendanceScenario.Late20Min:
                // CheckIn trễ 20 phút (vượt ngưỡng 15 phút)
                lateMin = 20;
                checkIn = plannedStart.AddMinutes(20);
                checkOut = plannedEnd;
                break;

            case AttendanceScenario.EarlyLeave20Min:
                // CheckOut sớm 20 phút (vượt ngưỡng 15 phút)
                earlyMin = 20;
                checkIn = plannedStart;
                checkOut = plannedEnd.AddMinutes(-20);
                break;

            case AttendanceScenario.Overtime45Min:
                // CheckOut trễ 45 phút (vượt ngưỡng 30 phút tính OT)
                otMin = 45;
                checkIn = plannedStart;
                checkOut = plannedEnd.AddMinutes(45);
                break;

            case AttendanceScenario.Normal:
            default:
                // Đúng giờ (check-in trước 2 phút, check-out đúng giờ)
                checkIn = plannedStart.AddMinutes(-2);
                checkOut = plannedEnd.AddMinutes(2);
                break;
        }

        // Số giờ làm việc thực tế = Giờ ca chuẩn - (muộn + về sớm) + OT
        var baseHours = shift.DurationHours;
        var actualHours = baseHours - Math.Round((decimal)(lateMin + earlyMin) / 60.00m, 2) + Math.Round((decimal)otMin / 60.00m, 2);

        return (lateMin, earlyMin, otMin, actualHours, checkIn, checkOut);
    }

    /// <summary>
    /// Khởi tạo một bản ghi EmployeeSchedule với đầy đủ dữ liệu điểm danh & phê duyệt
    /// </summary>
    private static EmployeeSchedule CreateSchedule(
        string tenantId,
        Guid branchId,
        Guid employeeId,
        ShiftConfig shift,
        DateTime workDate,
        DateTime checkIn,
        DateTime checkOut,
        int lateMin,
        int earlyMin,
        int otMin,
        decimal actualHours)
    {
        return new EmployeeSchedule
        {
            Id = Uuid7.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            EmployeeId = employeeId,
            ShiftConfigId = shift.Id,
            WorkDate = workDate.Date,

            // Snapshot thông tin ca làm việc
            ShiftCode = shift.ShiftCode,
            ShiftName = shift.ShiftName,
            PlannedStartTime = shift.StartTime,
            PlannedEndTime = shift.EndTime,
            DurationHours = shift.DurationHours,
            AllowedCheckInStart = shift.AllowedCheckInStart,
            AllowedCheckInEnd = shift.AllowedCheckInEnd,
            IsCheckInNextDay = shift.IsCheckInNextDay,

            // Chấm công thực tế
            CheckInTime = checkIn,
            CheckOutTime = checkOut,
            CheckInMethod = "FACE_ID",
            CheckOutMethod = "FACE_ID",
            CheckInFaceImageUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=300&q=80",
            CheckOutFaceImageUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=300&q=80",
            FaceMatchScore = 0.25m,

            // Giờ công & vi phạm
            LateMinutes = lateMin,
            EarlyLeaveMinutes = earlyMin,
            OvertimeMinutes = otMin,
            ActualWorkHours = actualHours,

            // Phê duyệt
            Status = ScheduleStatus.Completed,
            ApprovalStatus = ScheduleApprovalStatus.Approved,
            ApprovedBy = "usr_store_owner",
            ApprovedAt = workDate.Date.AddHours(23),

            CreatedAt = DateTime.UtcNow
        };
    }
}
