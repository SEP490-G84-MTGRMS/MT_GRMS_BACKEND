using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.HR.Employees;

public class EmployeeService : IEmployeeService
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public EmployeeService(
        IEmployeeRepository employeeRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _employeeRepository = employeeRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<EmployeeSummaryDto>> GetEmployeesPagedAsync(
        EmployeeQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _employeeRepository.GetPagedListAsync(filter, cancellationToken);

        var dtos = items.Select(e => new EmployeeSummaryDto(
            e.Id,
            e.EmployeeCode,
            e.FullName,
            e.Phone,
            e.Email,
            e.AvatarUrl,
            e.Address,
            e.StartDate,
            e.Note,
            e.BranchId,
            e.Branch?.Name ?? "Chưa gán",
            e.Status.ToString().ToLower(),
            e.SalaryType.ToString().ToLower(),
            e.BaseRate,
            e.OtWeekday,
            e.OtSunday,
            e.DebtAmount,
            e.UserId,
            e.User?.UserName,
            e.CreatedAt
        )).ToList();

        return new PagedResult<EmployeeSummaryDto>
        {
            Items = dtos,
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalItems = totalCount
        };
    }

    public async Task<EmployeeResponse> GetEmployeeByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException($"Không tìm thấy nhân viên với ID: {id}");
        }

        return MapToResponse(employee);
    }

    public async Task<EmployeeResponse?> GetEmployeeByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByUserIdAsync(userId, cancellationToken);
        if (employee == null && Guid.TryParse(userId, out var empGuid))
        {
            employee = await _employeeRepository.GetByIdAsync(empGuid, cancellationToken);
        }
        if (employee == null)
        {
            return null;
        }

        return MapToResponse(employee);
    }

    public async Task<EmployeeResponse> CreateEmployeeAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException("Họ và tên nhân viên không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.CitizenId))
        {
            throw new ArgumentException("Số CCCD không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            throw new ArgumentException("Số điện thoại không được để trống.");
        }

        if (request.BranchId == Guid.Empty)
        {
            throw new ArgumentException("Chi nhánh làm việc bắt buộc phải được chọn.");
        }

        if (request.BaseRate < 0)
        {
            throw new ArgumentException("Mức lương cơ bản không được nhỏ hơn 0.");
        }

        var normalizedCitizenId = request.CitizenId.Trim();
        var normalizedPhone = request.Phone.Trim();

        // 1. Kiểm tra trùng CCCD trong cùng Tenant
        var citizenExists = await _employeeRepository.ExistsAsync(
            e => e.CitizenId == normalizedCitizenId,
            cancellationToken);

        if (citizenExists)
        {
            throw new BusinessException("DUPLICATE_CITIZEN_ID", $"Số CCCD '{normalizedCitizenId}' đã tồn tại trong hệ thống.");
        }

        // 2. Kiểm tra trùng SĐT trong cùng Tenant
        var phoneExists = await _employeeRepository.ExistsAsync(
            e => e.Phone == normalizedPhone,
            cancellationToken);

        if (phoneExists)
        {
            throw new BusinessException("DUPLICATE_PHONE", $"Số điện thoại '{normalizedPhone}' đã được đăng ký cho nhân viên khác.");
        }

        // 3. Tự sinh mã nhân viên tuần tự dạng NV0001
        var maxCode = await _employeeRepository.GetMaxSequentialCodeAsync("NV", 6, cancellationToken);
        var nextNumber = 1;
        if (!string.IsNullOrEmpty(maxCode) && maxCode.StartsWith("NV", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(maxCode.Substring(2), out var currentNumber))
            {
                nextNumber = currentNumber + 1;
            }
        }
        var employeeCode = $"NV{nextNumber:D4}";
        while (await _employeeRepository.ExistsAsync(e => e.EmployeeCode == employeeCode, cancellationToken))
        {
            nextNumber++;
            employeeCode = $"NV{nextNumber:D4}";
        }

        var currentTenantId = _tenantContext.CurrentTenantId ?? "tenant_mt_mart_01";

        var employee = new Employee
        {
            TenantId = currentTenantId,
            BranchId = request.BranchId,
            EmployeeCode = employeeCode,
            FullName = request.FullName.Trim(),
            CitizenId = normalizedCitizenId,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            Phone = normalizedPhone,
            Email = request.Email?.Trim(),
            AvatarUrl = request.AvatarUrl?.Trim(),
            Address = request.Address?.Trim(),
            StartDate = request.StartDate,
            Note = request.Note?.Trim(),
            OtWeekday = request.OtWeekday?.Trim(),
            OtSunday = request.OtSunday?.Trim(),
            SalaryType = ParseSalaryType(request.SalaryType),
            BaseRate = request.BaseRate,
            BankAccountNumber = request.BankAccountNumber?.Trim(),
            BankName = request.BankName?.Trim(),
            BankAccountHolder = request.BankAccountHolder?.Trim(),
            Status = EmployeeStatus.Active
        };

        employee.Allowances = MapAllowances(request.Allowances, employee.Id, currentTenantId);

        // Tạo hoặc liên kết tài khoản Identity nếu có cung cấp UserName/UserId
        if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            var normalizedUsername = request.UserName.Trim();
            var existingUser = await _userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);
            if (existingUser != null)
            {
                throw new BusinessException("DUPLICATE_USERNAME", $"Tên đăng nhập '{normalizedUsername}' đã tồn tại trong hệ thống.");
            }

            var role = !string.IsNullOrWhiteSpace(request.Role) ? request.Role.Trim().ToUpper() : SystemRoles.Staff;
            if (role == "EMPLOYEE")
            {
                role = SystemRoles.Staff;
            }

            var newUser = new User
            {
                Id = Uuid7.NewString(),
                UserName = normalizedUsername,
                PasswordHash = !string.IsNullOrWhiteSpace(request.Password) ? request.Password : "Staff@123456",
                FullName = employee.FullName,
                Email = string.IsNullOrWhiteSpace(employee.Email) ? $"{normalizedUsername}@mtgrms.vn" : employee.Email,
                Phone = employee.Phone,
                IsActive = true
            };

            var tenantUser = new TenantUser
            {
                TenantId = currentTenantId,
                UserId = newUser.Id,
                Role = role,
                IsActive = true,
                AssignedAt = DateTime.UtcNow
            };
            newUser.TenantUsers.Add(tenantUser);

            await _userRepository.AddAsync(newUser, cancellationToken);
            employee.UserId = newUser.Id;
        }
        else if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            employee.UserId = request.UserId.Trim();
        }

        await _employeeRepository.AddAsync(employee, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Nạp lại chi tiết để có Branch name
        var created = await _employeeRepository.GetByIdAsync(employee.Id, cancellationToken);
        return MapToResponse(created ?? employee);
    }

    public async Task<EmployeeResponse> UpdateEmployeeAsync(
        Guid id,
        UpdateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException($"Không tìm thấy nhân viên với ID: {id}");
        }

        var normalizedCitizenId = request.CitizenId.Trim();
        var normalizedPhone = request.Phone.Trim();

        // 1. Kiểm tra trùng CCCD nếu đổi
        if (!string.Equals(employee.CitizenId, normalizedCitizenId, StringComparison.OrdinalIgnoreCase))
        {
            var citizenExists = await _employeeRepository.ExistsAsync(
                e => e.Id != id && e.CitizenId == normalizedCitizenId,
                cancellationToken);

            if (citizenExists)
            {
                throw new BusinessException("DUPLICATE_CITIZEN_ID", $"Số CCCD '{normalizedCitizenId}' đã tồn tại trong hệ thống.");
            }
        }

        // 2. Kiểm tra trùng SĐT nếu đổi
        if (!string.Equals(employee.Phone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
        {
            var phoneExists = await _employeeRepository.ExistsAsync(
                e => e.Id != id && e.Phone == normalizedPhone,
                cancellationToken);

            if (phoneExists)
            {
                throw new BusinessException("DUPLICATE_PHONE", $"Số điện thoại '{normalizedPhone}' đã được đăng ký cho nhân viên khác.");
            }
        }

        employee.FullName = request.FullName.Trim();
        employee.CitizenId = normalizedCitizenId;
        employee.Phone = normalizedPhone;
        employee.BranchId = request.BranchId;
        employee.Email = request.Email?.Trim();
        employee.Address = request.Address?.Trim();
        employee.StartDate = request.StartDate;
        employee.Note = request.Note?.Trim();
        employee.OtWeekday = request.OtWeekday?.Trim();
        employee.OtSunday = request.OtSunday?.Trim();
        employee.DateOfBirth = request.DateOfBirth;
        employee.Gender = request.Gender;
        employee.SalaryType = ParseSalaryType(request.SalaryType);
        employee.BaseRate = request.BaseRate;
        if (request.Allowances != null)
        {
            _employeeRepository.SyncAllowances(employee, request.Allowances, employee.TenantId);
        }
        employee.BankAccountNumber = request.BankAccountNumber?.Trim();
        employee.BankName = request.BankName?.Trim();
        employee.BankAccountHolder = request.BankAccountHolder?.Trim();
        if (request.AvatarUrl != null)
        {
            employee.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        }
        employee.UpdatedAt = DateTime.UtcNow;

        // Tạo hoặc cập nhật tài khoản Identity nếu có cung cấp UserName/UserId
        if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            var normalizedUsername = request.UserName.Trim();
            if (string.IsNullOrEmpty(employee.UserId))
            {
                var existingUser = await _userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);
                if (existingUser != null)
                {
                    throw new BusinessException("DUPLICATE_USERNAME", $"Tên đăng nhập '{normalizedUsername}' đã tồn tại trong hệ thống.");
                }

                var role = !string.IsNullOrWhiteSpace(request.Role) ? request.Role.Trim().ToUpper() : SystemRoles.Staff;
                if (role == "EMPLOYEE")
                {
                    role = SystemRoles.Staff;
                }

                var newUser = new User
                {
                    Id = Uuid7.NewString(),
                    UserName = normalizedUsername,
                    PasswordHash = !string.IsNullOrWhiteSpace(request.Password) ? request.Password : "Staff@123456",
                    FullName = employee.FullName,
                    Email = string.IsNullOrWhiteSpace(employee.Email) ? $"{normalizedUsername}@mtgrms.vn" : employee.Email,
                    Phone = employee.Phone,
                    IsActive = true
                };

                var tenantUser = new TenantUser
                {
                    TenantId = employee.TenantId,
                    UserId = newUser.Id,
                    Role = role,
                    IsActive = true,
                    AssignedAt = DateTime.UtcNow
                };
                newUser.TenantUsers.Add(tenantUser);

                await _userRepository.AddAsync(newUser, cancellationToken);
                employee.UserId = newUser.Id;
            }
            else
            {
                var user = await _userRepository.GetByIdAsync(employee.UserId, cancellationToken);
                if (user != null)
                {
                    if (!string.Equals(user.UserName, normalizedUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        var existingUser = await _userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);
                        if (existingUser != null && existingUser.Id != user.Id)
                        {
                            throw new BusinessException("DUPLICATE_USERNAME", $"Tên đăng nhập '{normalizedUsername}' đã tồn tại trong hệ thống.");
                        }
                        user.UserName = normalizedUsername;
                    }
                    if (!string.IsNullOrWhiteSpace(request.Password))
                    {
                        user.PasswordHash = request.Password;
                    }
                    user.FullName = employee.FullName;
                    _userRepository.Update(user);
                }
                else
                {
                    var existingUser = await _userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);
                    if (existingUser != null)
                    {
                        throw new BusinessException("DUPLICATE_USERNAME", $"Tên đăng nhập '{normalizedUsername}' đã tồn tại trong hệ thống.");
                    }

                    var role = !string.IsNullOrWhiteSpace(request.Role) ? request.Role.Trim().ToUpper() : "STAFF";
                    if (role == "EMPLOYEE")
                    {
                        role = "STAFF";
                    }

                    var newUser = new User
                    {
                        Id = Uuid7.NewString(),
                        UserName = normalizedUsername,
                        PasswordHash = !string.IsNullOrWhiteSpace(request.Password) ? request.Password : "Staff@123456",
                        FullName = employee.FullName,
                        Email = string.IsNullOrWhiteSpace(employee.Email) ? $"{normalizedUsername}@mtgrms.vn" : employee.Email,
                        Phone = employee.Phone,
                        IsActive = true
                    };

                    var tenantUser = new TenantUser
                    {
                        TenantId = employee.TenantId,
                        UserId = newUser.Id,
                        Role = role,
                        IsActive = true,
                        AssignedAt = DateTime.UtcNow
                    };
                    newUser.TenantUsers.Add(tenantUser);

                    await _userRepository.AddAsync(newUser, cancellationToken);
                    employee.UserId = newUser.Id;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            employee.UserId = request.UserId.Trim();
        }

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        return MapToResponse(updated ?? employee);
    }

    public async Task<bool> UpdateStatusAsync(
        Guid id,
        string status,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException($"Không tìm thấy nhân viên với ID: {id}");
        }

        var normalizedStatus = status.Trim().ToLower();
        if (normalizedStatus is not ("active" or "inactive"))
        {
            throw new ArgumentException("Trạng thái nhân viên chỉ chấp nhận 'active' hoặc 'inactive'.");
        }

        employee.Status = normalizedStatus == "active" ? EmployeeStatus.Active : EmployeeStatus.Inactive;
        employee.UpdatedAt = DateTime.UtcNow;

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteEmployeeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(id, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException($"Không tìm thấy nhân viên với ID: {id}");
        }

        // Business Rule: Chỉ được phép xóa nhân viên khi chưa lưu trữ dữ liệu ca làm việc hoặc phiếu lương
        var hasWorkHistory = await _employeeRepository.HasWorkOrPayrollHistoryAsync(id, cancellationToken);
        if (hasWorkHistory)
        {
            throw new BusinessException(
                "EMPLOYEE_HAS_WORK_HISTORY",
                $"Không thể xóa nhân viên \"{employee.FullName}\" ({employee.EmployeeCode}) vì đã có dữ liệu ca làm việc hoặc phiếu lương trong hệ thống. Bạn có thể chuyển trạng thái nhân viên sang \"Ngừng làm việc\" thay vì xóa.");
        }

        // 1. Xóa toàn bộ các khoản phụ cấp (EmployeeAllowances) của nhân viên
        if (employee.Allowances != null && employee.Allowances.Count > 0)
        {
            _employeeRepository.RemoveAllowances(employee.Allowances.ToList());
        }

        // 2. Soft delete hồ sơ nhân viên
        employee.IsDeleted = true;
        employee.Status = EmployeeStatus.Inactive;
        employee.UpdatedAt = DateTime.UtcNow;

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<string> CreateIdentityAccountAsync(
        Guid employeeId,
        CreateIdentityAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new ArgumentException("Tên đăng nhập không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Mật khẩu không được để trống.");
        }

        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException($"Không tìm thấy nhân viên với ID: {employeeId}");
        }

        if (!string.IsNullOrEmpty(employee.UserId))
        {
            throw new BusinessException("CONFLICT", "Nhân viên này đã được cấp tài khoản đăng nhập trước đó.");
        }

        var normalizedUsername = request.Username.Trim();
        var existingUser = await _userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);
        if (existingUser != null)
        {
            throw new BusinessException("DUPLICATE_USERNAME", $"Tên đăng nhập '{normalizedUsername}' đã tồn tại trong hệ thống.");
        }

        var currentTenantId = _tenantContext.CurrentTenantId ?? "tenant_mt_mart_01";
        var role = !string.IsNullOrWhiteSpace(request.Role) ? request.Role.Trim().ToUpper() : SystemRoles.Staff;
        if (role == "EMPLOYEE")
        {
            role = SystemRoles.Staff;
        }

        var displayName = !string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.DisplayName.Trim()
            : employee.FullName;

        var newUser = new User
        {
            Id = Uuid7.NewString(),
            UserName = normalizedUsername,
            PasswordHash = request.Password,
            FullName = displayName,
            Email = string.IsNullOrWhiteSpace(employee.Email) ? $"{normalizedUsername}@mtgrms.vn" : employee.Email,
            Phone = employee.Phone,
            IsActive = true
        };

        var tenantUser = new TenantUser
        {
            TenantId = currentTenantId,
            UserId = newUser.Id,
            Role = role,
            IsActive = true,
            AssignedAt = DateTime.UtcNow
        };

        newUser.TenantUsers.Add(tenantUser);

        await _userRepository.AddAsync(newUser, cancellationToken);

        employee.UserId = newUser.Id;
        employee.UpdatedAt = DateTime.UtcNow;
        _employeeRepository.Update(employee);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return newUser.Id;
    }

    public async Task<string> UpdateIdentityAccountAsync(
        Guid employeeId,
        CreateIdentityAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new ArgumentException("Tên đăng nhập không được để trống.");
        }

        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException($"Không tìm thấy nhân viên với ID: {employeeId}");
        }

        if (string.IsNullOrEmpty(employee.UserId))
        {
            throw new NotFoundException("Nhân viên này chưa được cấp tài khoản để cập nhật.");
        }

        var user = await _userRepository.GetByIdAsync(employee.UserId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException("Không tìm thấy tài khoản người dùng tương ứng.");
        }

        var normalizedUsername = request.Username.Trim();
        if (!string.Equals(user.UserName, normalizedUsername, StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await _userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                throw new BusinessException("DUPLICATE_USERNAME", $"Tên đăng nhập '{normalizedUsername}' đã tồn tại trong hệ thống.");
            }
            user.UserName = normalizedUsername;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = request.Password;
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.FullName = request.DisplayName.Trim();
        }

        _userRepository.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Id;
    }

    private static EmployeeResponse MapToResponse(Employee e)
    {
        var tenantUser = e.User?.TenantUsers?.FirstOrDefault();
        var role = tenantUser?.Role;

        return new EmployeeResponse(
            e.Id,
            e.EmployeeCode,
            e.FullName,
            e.CitizenId,
            e.DateOfBirth,
            e.Gender,
            e.Phone,
            e.Email,
            e.Address,
            e.StartDate,
            e.Note,
            e.AvatarUrl,
            e.BranchId,
            e.Branch?.Name ?? "Chưa gán",
            e.UserId,
            e.User?.UserName,
            role,
            e.SalaryType.ToString().ToLower(),
            e.BaseRate,
            e.OtWeekday,
            e.OtSunday,
            e.Allowances.Select(a => new EmployeeAllowanceDto(a.Name, a.Amount, a.Id)).ToList(),
            e.DebtAmount,
            e.Status.ToString().ToLower(),
            e.BankAccountNumber,
            e.BankName,
            e.BankAccountHolder,
            e.CreatedAt,
            e.UpdatedAt
        );
    }

    private static List<EmployeeAllowance> MapAllowances(List<EmployeeAllowanceDto>? allowances, Guid employeeId = default, string tenantId = "")
    {
        if (allowances == null || allowances.Count == 0)
        {
            return new List<EmployeeAllowance>();
        }

        return allowances
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new EmployeeAllowance
            {
                EmployeeId = employeeId,
                TenantId = tenantId,
                Name = a.Name.Trim(),
                Amount = Math.Max(0, a.Amount)
            })
            .ToList();
    }

    private static SalaryType ParseSalaryType(string? salaryType)
    {
        if (string.IsNullOrWhiteSpace(salaryType))
            return SalaryType.Monthly;

        if (Enum.TryParse<SalaryType>(salaryType, true, out var result))
            return result;

        return salaryType.Trim().ToLower() switch
        {
            "theothang" or "thang" or "monthly" => SalaryType.Monthly,
            "theogio" or "gio" or "hourly" => SalaryType.Hourly,
            "theoca" or "ca" or "shift" => SalaryType.Shift,
            _ => SalaryType.Monthly
        };
    }
}
