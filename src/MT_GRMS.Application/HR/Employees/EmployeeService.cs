using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Employees.Validators;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.HR.Employees;

public class EmployeeService : IEmployeeService
{
    private static DateTime VnNow => DateTime.UtcNow.AddHours(7);
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
            e.CreatedAt,
            !string.IsNullOrWhiteSpace(e.FaceDescriptor)
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

        var normalizedCitizenId = NormalizeCitizenId(request.CitizenId);
        var normalizedPhone = NormalizePhoneNumber(request.Phone);
        var phoneVariants = BuildPhoneVariants(normalizedPhone);
        var currentTenantId = _tenantContext.CurrentTenantId ?? "tenant_mt_mart_01";

        // 1. Kiểm tra trùng CCCD trong cùng Tenant
        var citizenExists = await _employeeRepository.ExistsAsync(
            e => e.CitizenId == normalizedCitizenId,
            cancellationToken);

        if (citizenExists)
        {
            throw new BusinessException("DUPLICATE_CITIZEN_ID", $"Số CCCD '{normalizedCitizenId}' đã tồn tại trong hệ thống.");
        }

        // 2. Kiểm tra trùng SĐT trong cùng Tenant (hỗ trợ cả định dạng 0xxx và +84xxx)
        var phoneExists = await _employeeRepository.ExistsAsync(
            e => phoneVariants.Contains(e.Phone),
            cancellationToken);

        if (phoneExists)
        {
            throw new BusinessException("DUPLICATE_PHONE", $"Số điện thoại '{normalizedPhone}' đã được đăng ký cho nhân viên khác.");
        }

        // 3. Xác định mã nhân viên: nếu nhập tay thì chuẩn hóa & kiểm tra trùng, nếu trống thì tự sinh NV0001
        string employeeCode;
        if (!string.IsNullOrWhiteSpace(request.EmployeeCode))
        {
            employeeCode = request.EmployeeCode.Trim().ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(employeeCode, @"^[A-Z0-9_-]{1,30}$"))
            {
                throw new ArgumentException("Mã nhân viên chỉ gồm chữ cái không dấu, số, gạch ngang/dưới và tối đa 30 ký tự.");
            }

            var codeExists = await _employeeRepository.ExistsAsync(
                e => e.EmployeeCode == employeeCode,
                cancellationToken);
            if (codeExists)
            {
                throw new BusinessException("EMPLOYEE_CODE_EXISTS", $"Mã nhân viên '{employeeCode}' đã tồn tại trong hệ thống.");
            }
        }
        else
        {
            var maxCode = await _employeeRepository.GetMaxSequentialCodeAsync("NV", 6, cancellationToken);
            var nextNumber = 1;
            if (!string.IsNullOrEmpty(maxCode) && maxCode.StartsWith("NV", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(maxCode.Substring(2), out var currentNumber))
                {
                    nextNumber = currentNumber + 1;
                }
            }
            employeeCode = $"NV{nextNumber:D4}";
            while (await _employeeRepository.ExistsIgnoringFiltersAsync(e => e.EmployeeCode == employeeCode, cancellationToken))
            {
                nextNumber++;
                employeeCode = $"NV{nextNumber:D4}";
            }
        }

        // Giải phóng CCCD/SĐT/EmployeeCode trên các bản ghi đã soft-delete (IsDeleted = true) nếu DB còn giữ unique index cũ
        await ReleaseDeletedEmployeeUniqueFieldsAsync(currentTenantId, normalizedCitizenId, normalizedPhone, employeeCode, null, cancellationToken);

        ValidateOtRates(request.OtWeekday, request.OtSunday);

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
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim(),
            FaceDescriptor = string.IsNullOrWhiteSpace(request.FaceDescriptor) ? null : request.FaceDescriptor.Trim(),
            FaceImageUrls = string.IsNullOrWhiteSpace(request.FaceImageUrls) ? null : request.FaceImageUrls.Trim(),
            FaceRegisteredAt = !string.IsNullOrWhiteSpace(request.FaceDescriptor) ? VnNow : null,
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            StartDate = request.StartDate ?? VnNow.Date,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            OtWeekday = string.IsNullOrWhiteSpace(request.OtWeekday) ? null : request.OtWeekday.Trim(),
            OtSunday = string.IsNullOrWhiteSpace(request.OtSunday) ? null : request.OtSunday.Trim(),
            SalaryType = ParseSalaryType(request.SalaryType),
            BaseRate = request.BaseRate,
            BankAccountNumber = string.IsNullOrWhiteSpace(request.BankAccountNumber) ? null : request.BankAccountNumber.Trim(),
            BankName = string.IsNullOrWhiteSpace(request.BankName) ? null : request.BankName.Trim(),
            BankAccountHolder = string.IsNullOrWhiteSpace(request.BankAccountHolder) ? null : request.BankAccountHolder.Trim().ToUpperInvariant(),
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

            var newUser = await BuildSafeEmployeeIdentityUserAsync(
                normalizedUsername,
                request.Password,
                employee.FullName,
                employee.Email,
                employee.Phone,
                cancellationToken);

            var tenantUser = new TenantUser
            {
                TenantId = currentTenantId,
                UserId = newUser.Id,
                Role = role,
                IsActive = true,
                AssignedAt = VnNow
            };
            newUser.TenantUsers.Add(tenantUser);

            await _userRepository.AddAsync(newUser, cancellationToken);
            employee.UserId = newUser.Id;
            employee.User = newUser;
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

        var normalizedCitizenId = NormalizeCitizenId(request.CitizenId);
        var normalizedPhone = NormalizePhoneNumber(request.Phone);
        var existingNormalizedCitizenId = NormalizeCitizenId(employee.CitizenId);
        var existingNormalizedPhone = NormalizePhoneNumber(employee.Phone);

        // 1. Kiểm tra trùng CCCD chỉ khi thực sự thay đổi sang CCCD khác
        if (!string.Equals(existingNormalizedCitizenId, normalizedCitizenId, StringComparison.OrdinalIgnoreCase))
        {
            var citizenExists = await _employeeRepository.ExistsAsync(
                e => e.Id != id && e.CitizenId == normalizedCitizenId,
                cancellationToken);

            if (citizenExists)
            {
                throw new BusinessException("DUPLICATE_CITIZEN_ID", $"Số CCCD '{normalizedCitizenId}' đã tồn tại trong hệ thống.");
            }
        }

        // 2. Kiểm tra trùng SĐT chỉ khi thực sự thay đổi sang SĐT khác (bỏ qua khác biệt định dạng +84/0/khoảng trắng của chính nhân viên đang sửa)
        if (!string.Equals(existingNormalizedPhone, normalizedPhone, StringComparison.OrdinalIgnoreCase))
        {
            var phoneVariants = BuildPhoneVariants(normalizedPhone);
            var phoneExists = await _employeeRepository.ExistsAsync(
                e => e.Id != id && phoneVariants.Contains(e.Phone),
                cancellationToken);

            if (phoneExists)
            {
                throw new BusinessException("DUPLICATE_PHONE", $"Số điện thoại '{normalizedPhone}' đã được đăng ký cho nhân viên khác.");
            }
        }

        await ReleaseDeletedEmployeeUniqueFieldsAsync(employee.TenantId, normalizedCitizenId, normalizedPhone, null, id, cancellationToken);

        ValidateOtRates(request.OtWeekday, request.OtSunday);

        employee.FullName = request.FullName.Trim();
        employee.CitizenId = normalizedCitizenId;
        employee.Phone = normalizedPhone;
        employee.BranchId = request.BranchId;
        employee.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        employee.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        employee.StartDate = request.StartDate;
        employee.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        employee.OtWeekday = string.IsNullOrWhiteSpace(request.OtWeekday) ? null : request.OtWeekday.Trim();
        employee.OtSunday = string.IsNullOrWhiteSpace(request.OtSunday) ? null : request.OtSunday.Trim();
        employee.DateOfBirth = request.DateOfBirth;
        employee.Gender = request.Gender;
        employee.SalaryType = ParseSalaryType(request.SalaryType);
        employee.BaseRate = request.BaseRate;
        if (request.Allowances != null)
        {
            _employeeRepository.SyncAllowances(employee, request.Allowances, employee.TenantId);
        }
        employee.BankAccountNumber = string.IsNullOrWhiteSpace(request.BankAccountNumber) ? null : request.BankAccountNumber.Trim();
        employee.BankName = string.IsNullOrWhiteSpace(request.BankName) ? null : request.BankName.Trim();
        employee.BankAccountHolder = string.IsNullOrWhiteSpace(request.BankAccountHolder) ? null : request.BankAccountHolder.Trim().ToUpperInvariant();
        if (request.AvatarUrl != null)
        {
            employee.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        }
        if (request.FaceDescriptor != null)
        {
            employee.FaceDescriptor = string.IsNullOrWhiteSpace(request.FaceDescriptor) ? null : request.FaceDescriptor.Trim();
            employee.FaceRegisteredAt = !string.IsNullOrWhiteSpace(request.FaceDescriptor) ? VnNow : null;
        }
        if (request.FaceImageUrls != null)
        {
            employee.FaceImageUrls = string.IsNullOrWhiteSpace(request.FaceImageUrls) ? null : request.FaceImageUrls.Trim();
        }
        employee.UpdatedAt = VnNow;

        // Tạo hoặc cập nhật tài khoản Identity nếu có cung cấp UserName/UserId
        if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            var normalizedUsername = request.UserName.Trim();
            if (string.IsNullOrEmpty(employee.UserId))
            {
                if (employee.Status == EmployeeStatus.Inactive)
                {
                    throw new BusinessException("EMPLOYEE_INACTIVE", "Không thể cấp tài khoản đăng nhập cho nhân viên đã ngừng làm việc.");
                }
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

                var newUser = await BuildSafeEmployeeIdentityUserAsync(
                    normalizedUsername,
                    request.Password,
                    employee.FullName,
                    employee.Email,
                    employee.Phone,
                    cancellationToken);

                var tenantUser = new TenantUser
                {
                    TenantId = employee.TenantId,
                    UserId = newUser.Id,
                    Role = role,
                    IsActive = true,
                    AssignedAt = VnNow
                };
                newUser.TenantUsers.Add(tenantUser);

                await _userRepository.AddAsync(newUser, cancellationToken);
                employee.UserId = newUser.Id;
                employee.User = newUser;
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
                    employee.User = user;
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

                    var newUser = await BuildSafeEmployeeIdentityUserAsync(
                        normalizedUsername,
                        request.Password,
                        employee.FullName,
                        employee.Email,
                        employee.Phone,
                        cancellationToken);

                    var tenantUser = new TenantUser
                    {
                        TenantId = employee.TenantId,
                        UserId = newUser.Id,
                        Role = role,
                        IsActive = true,
                        AssignedAt = VnNow
                    };
                    newUser.TenantUsers.Add(tenantUser);

                    await _userRepository.AddAsync(newUser, cancellationToken);
                    employee.UserId = newUser.Id;
                    employee.User = newUser;
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

        var isActivating = normalizedStatus == "active";
        employee.Status = isActivating ? EmployeeStatus.Active : EmployeeStatus.Inactive;
        employee.UpdatedAt = VnNow;

        await SyncLinkedUserLockAsync(employee, isActivating, cancellationToken);

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

        // Business Rule: Khi nhân viên đã có lịch làm việc hoặc phiếu lương, hệ thống không được phép xóa cứng/xóa khỏi danh sách,
        // mà chuyển sang trạng thái ngừng làm việc (Inactive) và giữ lại trong danh sách nhân viên (IsDeleted = false).
        var hasWorkHistory = await _employeeRepository.HasWorkOrPayrollHistoryAsync(id, cancellationToken);
        if (hasWorkHistory)
        {
            await SyncLinkedUserLockAsync(employee, false, cancellationToken);

            employee.IsDeleted = false;
            employee.Status = EmployeeStatus.Inactive;
            employee.UpdatedAt = VnNow;

            _employeeRepository.Update(employee);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            throw new BusinessException(
                "EMPLOYEE_HAS_WORK_HISTORY",
                $"Nhân viên \"{employee.FullName}\" ({employee.EmployeeCode}) đã có lịch làm việc hoặc phiếu lương nên hệ thống không được phép xóa cứng. Trạng thái nhân viên đã được chuyển sang \"Ngừng làm việc\" (Đã nghỉ) và tài khoản đăng nhập liên kết đã bị khóa.");
        }

        // 1. Xóa toàn bộ các khoản phụ cấp (EmployeeAllowances) của nhân viên chưa có lịch sử công/lương
        if (employee.Allowances != null && employee.Allowances.Count > 0)
        {
            _employeeRepository.RemoveAllowances(employee.Allowances.ToList());
        }

        // 2. Vô hiệu hóa tài khoản đăng nhập liên kết nếu có (BR-EMP-05)
        await SyncLinkedUserLockAsync(employee, false, cancellationToken);

        // 3. Soft delete hồ sơ nhân viên chưa có lịch sử công/lương
        employee.IsDeleted = true;
        employee.Status = EmployeeStatus.Inactive;
        employee.UpdatedAt = VnNow;

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task SyncLinkedUserLockAsync(Employee employee, bool isActive, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(employee.UserId))
        {
            return;
        }

        var user = await _userRepository.GetByIdAsync(employee.UserId, cancellationToken);
        if (user == null)
        {
            return;
        }

        user.IsActive = isActive;
        if (!isActive)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
        }

        if (user.TenantUsers != null)
        {
            foreach (var tenantUser in user.TenantUsers)
            {
                if (string.IsNullOrEmpty(employee.TenantId) || tenantUser.TenantId == employee.TenantId)
                {
                    tenantUser.IsActive = isActive;
                }
            }
        }

        _userRepository.Update(user);
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

        if (employee.Status == EmployeeStatus.Inactive)
        {
            throw new BusinessException("EMPLOYEE_INACTIVE", "Không thể cấp tài khoản đăng nhập cho nhân viên đã ngừng làm việc.");
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

        var newUser = await BuildSafeEmployeeIdentityUserAsync(
            normalizedUsername,
            request.Password,
            displayName,
            employee.Email,
            employee.Phone,
            cancellationToken);

        var tenantUser = new TenantUser
        {
            TenantId = currentTenantId,
            UserId = newUser.Id,
            Role = role,
            IsActive = true,
            AssignedAt = VnNow
        };

        newUser.TenantUsers.Add(tenantUser);

        await _userRepository.AddAsync(newUser, cancellationToken);

        employee.UserId = newUser.Id;
        employee.UpdatedAt = VnNow;
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

        if (employee.Status == EmployeeStatus.Inactive)
        {
            throw new BusinessException("EMPLOYEE_INACTIVE", "Không thể cập nhật tài khoản cho nhân viên đã ngừng làm việc (tài khoản đang bị khóa).");
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
            e.UpdatedAt,
            e.FaceDescriptor,
            e.FaceImageUrls,
            e.FaceRegisteredAt
        );
    }

    public async Task<bool> RegisterFaceDescriptorAsync(
        Guid employeeId,
        string faceDescriptor,
        string? faceImageUrls = null,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken);
        if (employee == null)
        {
            throw new NotFoundException($"Không tìm thấy nhân viên với ID '{employeeId}'.");
        }

        employee.FaceDescriptor = string.IsNullOrWhiteSpace(faceDescriptor) ? null : faceDescriptor.Trim();
        if (faceImageUrls != null)
        {
            employee.FaceImageUrls = string.IsNullOrWhiteSpace(faceImageUrls) ? null : faceImageUrls.Trim();
        }
        employee.FaceRegisteredAt = !string.IsNullOrWhiteSpace(faceDescriptor) ? VnNow : null;
        employee.UpdatedAt = VnNow;

        _employeeRepository.Update(employee);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
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

    private static string NormalizeCitizenId(string? citizenId)
    {
        if (string.IsNullOrWhiteSpace(citizenId))
            return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(citizenId.Trim(), @"\s+", "");
    }

    private static string NormalizePhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var cleaned = phone.Trim()
            .Replace(" ", "")
            .Replace(".", "")
            .Replace("-", "");

        if (cleaned.StartsWith("+84", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = "0" + cleaned.Substring(3);
        }
        else if (cleaned.StartsWith("84") && cleaned.Length == 11)
        {
            cleaned = "0" + cleaned.Substring(2);
        }

        return cleaned;
    }

    private static List<string> BuildPhoneVariants(string normalizedPhone)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedPhone };
        if (normalizedPhone.StartsWith("0") && normalizedPhone.Length == 10)
        {
            variants.Add("+84" + normalizedPhone.Substring(1));
            variants.Add("84" + normalizedPhone.Substring(1));
        }
        return variants.ToList();
    }

    private async Task ReleaseDeletedEmployeeUniqueFieldsAsync(
        string tenantId,
        string citizenId,
        string phone,
        string? employeeCode,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var deletedMatch = await _employeeRepository.GetDeletedDuplicateAsync(
            tenantId,
            citizenId,
            phone,
            employeeCode,
            excludeId,
            cancellationToken);

        while (deletedMatch != null)
        {
            var suffix = $"_del_{deletedMatch.Id.ToString("N")[..6]}";
            if (deletedMatch.CitizenId == citizenId)
            {
                var raw = deletedMatch.CitizenId + suffix;
                deletedMatch.CitizenId = raw.Length <= 20 ? raw : raw[..20];
            }
            if (deletedMatch.Phone == phone)
            {
                var raw = deletedMatch.Phone + suffix;
                deletedMatch.Phone = raw.Length <= 20 ? raw : raw[..20];
            }
            if (!string.IsNullOrEmpty(employeeCode) && deletedMatch.EmployeeCode == employeeCode)
            {
                var raw = deletedMatch.EmployeeCode + suffix;
                deletedMatch.EmployeeCode = raw.Length <= 30 ? raw : raw[..30];
            }

            _employeeRepository.Update(deletedMatch);

            deletedMatch = await _employeeRepository.GetDeletedDuplicateAsync(
                tenantId,
                citizenId,
                phone,
                employeeCode,
                deletedMatch.Id,
                cancellationToken);
        }
    }

    private async Task<User> BuildSafeEmployeeIdentityUserAsync(
        string normalizedUsername,
        string? password,
        string fullName,
        string? employeeEmail,
        string? employeePhone,
        CancellationToken cancellationToken)
    {
        var newUserId = Uuid7.NewString();
        var candidateEmail = string.IsNullOrWhiteSpace(employeeEmail)
            ? $"{normalizedUsername}@mtgrms.vn"
            : employeeEmail.Trim();
        var normalizedEmail = candidateEmail.Trim().ToLowerInvariant();

        // Tránh vi phạm Unique Index IX_Users_Email, IX_Users_NormalizedEmail và IX_Users_Phone nếu SĐT/Email đã thuộc về một User khác (ví dụ StoreOwner hoặc tài khoản cũ)
        var emailConflict = await _userRepository.FindAsync(
            u => u.NormalizedEmail == normalizedEmail || u.Email.ToLower() == normalizedEmail,
            cancellationToken);
        if (emailConflict.Count > 0)
        {
            candidateEmail = $"{normalizedUsername}.{newUserId[..6]}@mtgrms.vn";
            normalizedEmail = candidateEmail.ToLowerInvariant();
        }

        string? candidatePhone = string.IsNullOrWhiteSpace(employeePhone) ? null : NormalizePhoneNumber(employeePhone);
        if (!string.IsNullOrEmpty(candidatePhone))
        {
            var phoneVariants = BuildPhoneVariants(candidatePhone);
            var phoneConflict = await _userRepository.FindAsync(
                u => u.Phone != null && phoneVariants.Contains(u.Phone),
                cancellationToken);
            if (phoneConflict.Count > 0)
            {
                candidatePhone = null;
            }
        }

        return new User
        {
            Id = newUserId,
            UserName = normalizedUsername,
            PasswordHash = !string.IsNullOrWhiteSpace(password) ? password : "Staff@123456",
            FullName = fullName,
            Email = candidateEmail,
            NormalizedEmail = normalizedEmail,
            Phone = candidatePhone,
            NormalizedPhone = candidatePhone,
            IsActive = true
        };
    }

    private static void ValidateOtRates(string? otWeekday, string? otSunday)
    {
        if (!string.IsNullOrWhiteSpace(otWeekday))
        {
            var raw = otWeekday.Trim().TrimEnd('%');
            if (!decimal.TryParse(raw, out var val) || val < 100m)
            {
                throw new BusinessException("INVALID_OT_RATE", "Hệ số làm thêm giờ (OT) ngày thường không được nhỏ hơn 100%.");
            }
        }

        if (!string.IsNullOrWhiteSpace(otSunday))
        {
            var raw = otSunday.Trim().TrimEnd('%');
            if (!decimal.TryParse(raw, out var val) || val < 100m)
            {
                throw new BusinessException("INVALID_OT_RATE", "Hệ số làm thêm giờ (OT) Chủ nhật / Nghỉ tuần không được nhỏ hơn 100%.");
            }
        }
    }
}
