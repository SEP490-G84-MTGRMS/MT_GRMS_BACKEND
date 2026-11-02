using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Domain.Common;
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
            e.BranchId,
            e.Branch?.Name ?? "Chưa gán",
            e.JobTitle,
            e.Status.ToString().ToLower(),
            e.SalaryType,
            e.BaseRate,
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

    public async Task<EmployeeResponse> CreateEmployeeAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
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
            JobTitle = request.JobTitle?.Trim(),
            SalaryType = string.IsNullOrWhiteSpace(request.SalaryType) ? "monthly" : request.SalaryType.Trim().ToLower(),
            BaseRate = request.BaseRate,
            Allowances = MapAllowances(request.Allowances),
            BankAccountNumber = request.BankAccountNumber?.Trim(),
            BankName = request.BankName?.Trim(),
            BankAccountHolder = request.BankAccountHolder?.Trim(),
            Status = EmployeeStatus.Active
        };

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
        employee.JobTitle = request.JobTitle?.Trim();
        employee.DateOfBirth = request.DateOfBirth;
        employee.Gender = request.Gender;
        employee.SalaryType = string.IsNullOrWhiteSpace(request.SalaryType) ? "monthly" : request.SalaryType.Trim().ToLower();
        employee.BaseRate = request.BaseRate;
        employee.Allowances = MapAllowances(request.Allowances);
        employee.BankAccountNumber = request.BankAccountNumber?.Trim();
        employee.BankName = request.BankName?.Trim();
        employee.BankAccountHolder = request.BankAccountHolder?.Trim();
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            employee.AvatarUrl = request.AvatarUrl.Trim();
        }
        employee.UpdatedAt = DateTime.UtcNow;

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

        // Soft delete
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
        var role = request.Role.Trim().ToUpper();

        var newUser = new User
        {
            Id = Uuid7.NewString(),
            UserName = normalizedUsername,
            PasswordHash = request.Password,
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
        employee.UpdatedAt = DateTime.UtcNow;
        _employeeRepository.Update(employee);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return newUser.Id;
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
            e.AvatarUrl,
            e.JobTitle,
            e.BranchId,
            e.Branch?.Name ?? "Chưa gán",
            e.UserId,
            e.User?.UserName,
            role,
            e.SalaryType,
            e.BaseRate,
            e.Allowances.Select(a => new EmployeeAllowanceDto(a.Name, a.Amount)).ToList(),
            e.DebtAmount,
            e.Status.ToString().ToLower(),
            e.BankAccountNumber,
            e.BankName,
            e.BankAccountHolder,
            e.CreatedAt,
            e.UpdatedAt
        );
    }

    private static List<EmployeeAllowance> MapAllowances(List<EmployeeAllowanceDto>? allowances)
    {
        if (allowances == null || allowances.Count == 0)
        {
            return new List<EmployeeAllowance>();
        }

        return allowances
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new EmployeeAllowance
            {
                Name = a.Name.Trim(),
                Amount = Math.Max(0, a.Amount)
            })
            .ToList();
    }
}
