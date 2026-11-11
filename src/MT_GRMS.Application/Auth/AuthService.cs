using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Auth;

/// <summary>
/// Service xử lý xác thực người dùng, Email OTP (2FA Đăng nhập & Đăng ký) và cấp phát JWT token
/// </summary>
public class AuthService : IAuthService
{
    private const int MaxOtpFailedAttempts = 5;

    private sealed record RegistrationOtpEntry(string OtpCode, DateTime ExpiresAtUtc, int FailedAttempts = 0);
    private sealed record LoginOtpEntry(string UserId, string? TenantCode, string OtpCode, DateTime ExpiresAtUtc, int FailedAttempts = 0);

    private static readonly ConcurrentDictionary<string, RegistrationOtpEntry> RegistrationOtpStore = new();
    private static readonly ConcurrentDictionary<string, LoginOtpEntry> LoginOtpStore = new();

    private readonly IUserRepository _userRepository;
    private readonly IConfiguration? _configuration;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService? _emailService;

    public AuthService(
        IUserRepository userRepository,
        IConfiguration? configuration = null,
        IPasswordHasher? passwordHasher = null,
        IEmailService? emailService = null)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _passwordHasher = passwordHasher ?? new PasswordHasher();
        _emailService = emailService;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Tài khoản và mật khẩu không được để trống.");
        }

        var user = await _userRepository.GetByUsernameAsync(request.UsernameOrEmail.Trim(), cancellationToken)
            ?? throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");

        await VerifyPasswordAsync(user, request.Password, cancellationToken);

        return await IssueLoginSessionAsync(user, request.TenantCode, cancellationToken);
    }

    public async Task<SendLoginOtpResponse> SendLoginOtpAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Tài khoản và mật khẩu không được để trống.");
        }

        var user = await _userRepository.GetByUsernameAsync(request.UsernameOrEmail.Trim(), cancellationToken)
            ?? throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");

        await VerifyPasswordAsync(user, request.Password, cancellationToken);

        var targetEmail = !string.IsNullOrWhiteSpace(user.Email)
            ? user.Email.Trim()
            : (request.UsernameOrEmail.Contains('@') ? request.UsernameOrEmail.Trim() : $"{user.Username}@mtgrms.vn");

        var fullName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var tempToken = $"otp_login_{Guid.NewGuid():N}";
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(5);

        LoginOtpStore[tempToken] = new LoginOtpEntry(
            UserId: user.Id,
            TenantCode: request.TenantCode,
            OtpCode: otpCode,
            ExpiresAtUtc: expiresAtUtc,
            FailedAttempts: 0);

        var isRealEmailSent = false;
        if (_emailService != null)
        {
            isRealEmailSent = await _emailService.SendLoginOtpAsync(targetEmail, fullName, otpCode, cancellationToken);
        }

        var maskedEmail = MaskEmailDisplay(targetEmail);
        var message = isRealEmailSent
            ? $"Mã xác thực OTP 6 chữ số đã được gửi tới email {maskedEmail}."
            : $"Đã tạo mã xác thực OTP đăng nhập cho email {maskedEmail}.";

        return new SendLoginOtpResponse(
            RequiresOtp: true,
            TempToken: tempToken,
            MaskedEmail: maskedEmail,
            TargetEmail: targetEmail,
            ExpiresInSeconds: 300,
            IsRealEmailSent: isRealEmailSent,
            Message: message,
            DevOtpCode: isRealEmailSent ? null : otpCode
        );
    }

    public async Task<LoginResponse> VerifyLoginOtpAsync(VerifyLoginOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.TempToken) || string.IsNullOrWhiteSpace(request.OtpCode))
        {
            throw new ArgumentException("Phiên xác thực và mã OTP không được để trống.");
        }

        var tempToken = request.TempToken.Trim();
        if (!LoginOtpStore.TryGetValue(tempToken, out var entry))
        {
            throw new ArgumentException("Phiên xác thực OTP không tồn tại hoặc đã hết hạn. Vui lòng đăng nhập lại.");
        }

        if (entry.ExpiresAtUtc < DateTime.UtcNow)
        {
            LoginOtpStore.TryRemove(tempToken, out _);
            throw new ArgumentException("Mã OTP đăng nhập đã hết hạn (quá 5 phút). Vui lòng đăng nhập lại để nhận mã mới.");
        }

        var cleanOtp = request.OtpCode.Trim();
        if (!string.Equals(entry.OtpCode, cleanOtp, StringComparison.Ordinal))
        {
            var nextFailures = entry.FailedAttempts + 1;
            if (nextFailures >= MaxOtpFailedAttempts)
            {
                LoginOtpStore.TryRemove(tempToken, out _);
                throw new ArgumentException("Bạn đã nhập sai mã OTP quá 5 lần. Phiên xác thực đã bị hủy, vui lòng đăng nhập lại.");
            }

            LoginOtpStore[tempToken] = entry with { FailedAttempts = nextFailures };
            throw new ArgumentException($"Mã xác thực Email OTP không chính xác (còn {MaxOtpFailedAttempts - nextFailures} lần thử).");
        }

        // Xóa mã OTP ngay sau khi xác thực thành công (One-Time-Use)
        LoginOtpStore.TryRemove(tempToken, out _);

        var user = await _userRepository.GetByIdAsync(entry.UserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Tài khoản người dùng không tồn tại.");

        return await IssueLoginSessionAsync(user, entry.TenantCode, cancellationToken);
    }

    private async Task<LoginResponse> IssueLoginSessionAsync(User user, string? tenantCode, CancellationToken cancellationToken)
    {
        var (tenantId, tenantUser) = ResolveTenant(user, tenantCode);
        var userRole = ResolveUserRole(user, tenantUser);
        var role = userRole.ToStringRole();
        var isAdmin = userRole == UserRole.Admin;
        var branchId = !string.IsNullOrWhiteSpace(tenantId)
            ? await _userRepository.GetEmployeeBranchIdAsync(user.Id, tenantId, cancellationToken)
            : null;

        var userInfo = new UserInfo(
            Id: user.Id,
            Username: user.Username,
            FullName: string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
            Email: string.IsNullOrWhiteSpace(user.Email) ? $"{user.Username}@mtgrms.vn" : user.Email,
            RoleName: role,
            BranchId: branchId,
            TenantId: tenantId,
            IsAdmin: isAdmin
        );

        var expiresMinutes = int.TryParse(_configuration?["Jwt:ExpiresMinutes"], out var parsedMins) ? parsedMins : 120;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var accessToken = GenerateJwtToken(user.Id, user.Username, userInfo.Email, role, tenantId, isAdmin, expiresAt, branchId);
        var refreshToken = $"mt_refresh_{Guid.NewGuid():N}";

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        user.LastLoginAt = DateTime.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: expiresAt,
            User: userInfo
        );
    }

    /// <summary>
    /// Kiểm tra mật khẩu (hỗ trợ PBKDF2 PasswordHasher, SHA256, Plaintext và demo password tiện lợi)
    /// Tự động nâng cấp hash lên PBKDF2 nếu đăng nhập bằng thuật toán cũ thành công
    /// </summary>
    private async Task VerifyPasswordAsync(User user, string requestPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");
        }

        bool isPasswordValid = _passwordHasher.VerifyPassword(user.PasswordHash, requestPassword, out var rehashNeeded);

        // Fallback 1: Kiểm tra SHA256 hash từ dữ liệu seed cũ
        if (!isPasswordValid)
        {
            try
            {
                var hashedInput = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestPassword))).ToLowerInvariant();
                if (string.Equals(user.PasswordHash, hashedInput, StringComparison.OrdinalIgnoreCase))
                {
                    isPasswordValid = true;
                    rehashNeeded = true;
                }
            }
            catch { }
        }

        // Fallback 2: Kiểm tra plaintext trực tiếp
        if (!isPasswordValid && string.Equals(user.PasswordHash, requestPassword, StringComparison.Ordinal))
        {
            isPasswordValid = true;
            rehashNeeded = true;
        }

        // Fallback 3: Cho phép mật khẩu demo "password123" hoặc "123456" cho tài khoản seed
        if (!isPasswordValid && (requestPassword is "password123" or "123456"))
        {
            if (user.UserName.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                user.UserName.Equals("manager01", StringComparison.OrdinalIgnoreCase) ||
                user.UserName.Equals("cashier01", StringComparison.OrdinalIgnoreCase))
            {
                isPasswordValid = true;
            }
        }

        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");
        }

        if (rehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(requestPassword);
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private static (string TenantId, TenantUser? TenantUser) ResolveTenant(User user, string? tenantCode)
    {
        var tenantUser = user.TenantUsers?.FirstOrDefault(tu => !string.IsNullOrWhiteSpace(tenantCode) && tu.TenantId == tenantCode)
                         ?? user.TenantUsers?.FirstOrDefault();

        var tenantId = tenantUser?.TenantId ?? (!string.IsNullOrWhiteSpace(tenantCode) ? tenantCode : "tenant_mt_mart_01");
        if (tenantId is "default-tenant" or "TENANT-HCM-01" or "TENANT-DEFAULT" or "DEFAULT")
        {
            tenantId = "tenant_mt_mart_01";
        }

        return (tenantId, tenantUser);
    }

    private static UserRole ResolveUserRole(User user, TenantUser? tenantUser)
    {
        if (user.IsAdmin || user.UserName.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            return UserRole.Admin;
        }

        return UserRoleNames.FromString(tenantUser?.Role ?? UserRoleNames.StoreOwner);
    }

    public async Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ArgumentException("RefreshToken không được để trống.");
        }

        var matchingUsers = await _userRepository.FindAsync(u => u.RefreshToken == request.RefreshToken, cancellationToken);
        var user = matchingUsers.FirstOrDefault();

        if (user == null || user.RefreshTokenExpiryTime == null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("Phiên đăng nhập đã hết hạn hoặc RefreshToken không hợp lệ. Vui lòng đăng nhập lại.");
        }

        var (tenantId, tenantUser) = ResolveTenant(user, null);
        var userRole = ResolveUserRole(user, tenantUser);
        var role = userRole.ToStringRole();
        var isAdmin = userRole == UserRole.Admin;
        var branchId = !string.IsNullOrWhiteSpace(tenantId)
            ? await _userRepository.GetEmployeeBranchIdAsync(user.Id, tenantId, cancellationToken)
            : null;

        var userInfo = new UserInfo(
            Id: user.Id,
            Username: user.Username,
            FullName: string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
            Email: string.IsNullOrWhiteSpace(user.Email) ? $"{user.Username}@mtgrms.vn" : user.Email,
            RoleName: role,
            BranchId: branchId,
            TenantId: tenantId,
            IsAdmin: isAdmin
        );

        var expiresMinutes = int.TryParse(_configuration?["Jwt:ExpiresMinutes"], out var parsedMins) ? parsedMins : 120;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var accessToken = GenerateJwtToken(user.Id, user.Username, userInfo.Email, role, tenantId, isAdmin, expiresAt, branchId);

        var newRefreshToken = $"mt_refresh_{Guid.NewGuid():N}";
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: newRefreshToken,
            ExpiresAt: expiresAt,
            User: userInfo
        );
    }

    public async Task<UserInfo?> GetCurrentUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || !user.IsActive) return null;

        var (tenantId, tenantUser) = ResolveTenant(user, null);
        var userRole = ResolveUserRole(user, tenantUser);
        var role = userRole.ToStringRole();
        var isAdmin = userRole == UserRole.Admin;
        var branchId = !string.IsNullOrWhiteSpace(tenantId)
            ? await _userRepository.GetEmployeeBranchIdAsync(user.Id, tenantId, cancellationToken)
            : null;

        return new UserInfo(
            Id: user.Id,
            Username: user.Username,
            FullName: string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
            Email: string.IsNullOrWhiteSpace(user.Email) ? $"{user.Username}@mtgrms.vn" : user.Email,
            RoleName: role,
            BranchId: branchId,
            TenantId: tenantId,
            IsAdmin: isAdmin
        );
    }

    public async Task RevokeRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<LoginResponse> RegisterTenantAsync(RegisterTenantRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            throw new ArgumentException("Họ và tên không được để trống.");
        if (string.IsNullOrWhiteSpace(request.StoreName))
            throw new ArgumentException("Tên cửa hàng không được để trống.");
        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new ArgumentException("Số điện thoại không được để trống.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            throw new ArgumentException("Mật khẩu phải có tối thiểu 6 ký tự.");

        var isDuplicate = await _userRepository.ExistsByPhoneOrEmailAsync(request.Phone.Trim(), request.Email, cancellationToken);
        if (isDuplicate)
        {
            throw new ArgumentException("Số điện thoại hoặc email đã được đăng ký trong hệ thống.");
        }

        var otpKey = ResolveRegistrationOtpKey(request.Email, request.Phone);
        if (!string.IsNullOrWhiteSpace(request.OtpCode) || (!string.IsNullOrWhiteSpace(otpKey) && RegistrationOtpStore.ContainsKey(otpKey)))
        {
            if (string.IsNullOrWhiteSpace(request.OtpCode))
            {
                throw new ArgumentException("Vui lòng nhập mã xác thực OTP đã gửi đến email của bạn.");
            }

            await VerifyRegistrationOtpAsync(new VerifyRegistrationOtpRequest(otpKey, request.OtpCode), cancellationToken);
            RegistrationOtpStore.TryRemove(otpKey, out _);
        }

        var rawSlug = request.StoreName.Trim().ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(".", "-")
            .Replace("/", "-");
        var cleanSlug = Regex.Replace(rawSlug, @"[^a-z0-9\-]", "");
        if (string.IsNullOrWhiteSpace(cleanSlug)) cleanSlug = "tiemmoi";
        var uniqueSuffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString("x");
        var tenantId = $"tenant_{cleanSlug}_{uniqueSuffix}";
        var tenantCode = $"{cleanSlug.ToUpperInvariant()}-{uniqueSuffix.ToUpperInvariant()}";

        var tenant = new Tenant
        {
            Id = tenantId,
            Name = request.StoreName.Trim(),
            Subdomain = $"{cleanSlug}-{uniqueSuffix}",
            Code = tenantCode.Length > 20 ? tenantCode.Substring(0, 20) : tenantCode,
            Hotline = request.Phone.Trim(),
            Status = "ACTIVE",
            CurrentPlan = "TRIAL",
            SubscriptionStatus = "TRIAL",
            HasStaffManagementAddon = true,
            WalletBalance = 0m,
            SubscriptionExpiresAt = DateTime.UtcNow.AddDays(3)
        };

        var userId = $"usr_{Guid.NewGuid():N}";
        var userEmail = string.IsNullOrWhiteSpace(request.Email) ? $"{request.Phone.Trim()}@mtgrms.vn" : request.Email.Trim();
        var refreshToken = $"mt_refresh_{Guid.NewGuid():N}";
        var user = new User
        {
            Id = userId,
            UserName = request.Phone.Trim(),
            FullName = request.FullName.Trim(),
            Email = userEmail,
            Phone = request.Phone.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            IsActive = true,
            IsAdmin = false,
            IsEmailVerified = true,
            RefreshToken = refreshToken,
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchCode = "BR-01",
            Name = $"{request.StoreName.Trim()} - Chi Nhánh Chính",
            Phone = request.Phone.Trim(),
            IsActive = true
        };

        await _userRepository.RegisterStoreOwnerAsync(user, tenant, branch, cancellationToken);

        var expiresMinutes = int.TryParse(_configuration?["Jwt:ExpiresMinutes"], out var parsedMins) ? parsedMins : 120;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var accessToken = GenerateJwtToken(user.Id, user.UserName, user.Email, UserRoleNames.StoreOwner, tenant.Id, false, expiresAt, branch.Id);

        var userInfo = new UserInfo(
            Id: user.Id,
            Username: user.UserName,
            FullName: user.FullName,
            Email: user.Email,
            RoleName: UserRoleNames.StoreOwner,
            BranchId: branch.Id,
            TenantId: tenant.Id,
            IsAdmin: false
        );

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: expiresAt,
            User: userInfo
        );
    }

    private string GenerateJwtToken(string userId, string username, string email, string role, string tenantId, bool isAdmin, DateTime expiresAt, Guid? branchId = null)
    {
        var secretKey = _configuration?["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            secretKey = "MT_GRMS_Super_Secret_Key_For_JWT_Authentication_2026_FPTU_Capstone_Project!";
        }

        var issuer = _configuration?["Jwt:Issuer"] ?? "MTGRMS_API";
        var audience = _configuration?["Jwt:Audience"] ?? "MTGRMS_CLIENT";

        var rolesToAdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role };
        if (role.Equals("STORE_OWNER", StringComparison.OrdinalIgnoreCase) || role.Equals("StoreOwner", StringComparison.OrdinalIgnoreCase))
        {
            rolesToAdd.Add("StoreOwner");
            rolesToAdd.Add("STORE_OWNER");
        }
        else if (role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) || role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            rolesToAdd.Add("Admin");
            rolesToAdd.Add("ADMIN");
        }
        else
        {
            rolesToAdd.Add("Employee");
            rolesToAdd.Add("EMPLOYEE");
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Email, email),
            new Claim("role", role),
            new Claim("tenant_id", tenantId),
            new Claim("is_admin", isAdmin ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (branchId.HasValue)
        {
            claims.Add(new Claim("branch_id", branchId.Value.ToString()));
        }

        foreach (var r in rolesToAdd)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ArgumentException("Vui lòng nhập địa chỉ email hợp lệ.");
        }

        var cleanInput = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByUsernameAsync(cleanInput, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tài khoản với thông tin đã cung cấp.");
        }

        var targetEmail = string.IsNullOrWhiteSpace(user.Email) ? cleanInput : user.Email;
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        user.PasswordResetOtp = otpCode;
        user.OtpExpiry = DateTime.UtcNow.AddMinutes(10);

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        if (_emailService != null)
        {
            await _emailService.SendPasswordResetOtpAsync(targetEmail, user.FullName, otpCode, cancellationToken);
        }
    }

    public async Task<bool> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.OtpCode))
        {
            throw new ArgumentException("Email và mã OTP không được để trống.");
        }

        var cleanInput = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByUsernameAsync(cleanInput, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tài khoản với thông tin đã cung cấp.");
        }

        if (string.IsNullOrWhiteSpace(user.PasswordResetOtp) || user.OtpExpiry == null)
        {
            throw new ArgumentException("Mã OTP không tồn tại hoặc chưa được yêu cầu.");
        }

        if (user.OtpExpiry < DateTime.UtcNow)
        {
            throw new ArgumentException("Mã OTP đã hết hạn sử dụng (quá 10 phút). Vui lòng yêu cầu mã mới.");
        }

        if (user.PasswordResetOtp.Trim() != request.OtpCode.Trim())
        {
            throw new ArgumentException("Mã OTP không chính xác. Vui lòng kiểm tra lại.");
        }

        return true;
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.OtpCode) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            throw new ArgumentException("Vui lòng điền đầy đủ email, mã OTP và mật khẩu mới.");
        }

        if (request.NewPassword.Length < 8)
        {
            throw new ArgumentException("Mật khẩu mới phải có tối thiểu 8 ký tự.");
        }

        await VerifyOtpAsync(new VerifyOtpRequest(request.Email, request.OtpCode), cancellationToken);

        var cleanInput = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByUsernameAsync(cleanInput, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.PasswordResetOtp = null;
        user.OtpExpiry = null;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<SendRegistrationOtpResponse> SendRegistrationOtpAsync(
        SendRegistrationOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Phone)))
        {
            throw new ArgumentException("Vui lòng nhập địa chỉ email hợp lệ để nhận mã xác thực OTP.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains('@'))
        {
            throw new ArgumentException("Địa chỉ email không đúng định dạng.");
        }

        var phoneTrimmed = request.Phone?.Trim() ?? string.Empty;
        var emailTrimmed = request.Email?.Trim().ToLowerInvariant();

        var isDuplicate = await _userRepository.ExistsByPhoneOrEmailAsync(
            phoneTrimmed,
            emailTrimmed,
            cancellationToken);

        if (isDuplicate)
        {
            throw new ArgumentException("Số điện thoại hoặc email này đã được đăng ký cửa hàng trên hệ thống.");
        }

        var otpKey = ResolveRegistrationOtpKey(request.Email, request.Phone);
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        RegistrationOtpStore[otpKey] = new RegistrationOtpEntry(otpCode, expiresAtUtc, 0);

        var targetEmail = !string.IsNullOrWhiteSpace(emailTrimmed)
            ? emailTrimmed
            : $"{NormalizePhoneKey(phoneTrimmed)}@mtgrms.vn";

        var isRealEmailSent = false;
        if (_emailService != null)
        {
            isRealEmailSent = await _emailService.SendRegistrationOtpAsync(
                targetEmail,
                request.FullName ?? "Chủ cửa hàng",
                request.StoreName ?? "MT-GRMS",
                otpCode,
                cancellationToken);
        }

        var maskedEmail = MaskEmailDisplay(targetEmail);
        var message = isRealEmailSent
            ? $"Mã xác thực Email OTP đã được gửi tới hộp thư {maskedEmail}."
            : $"Đã tạo mã xác thực Email OTP cho {maskedEmail}.";

        return new SendRegistrationOtpResponse(
            Success: true,
            Message: message,
            MaskedEmail: maskedEmail,
            ExpiresInSeconds: 300,
            IsRealEmailSent: isRealEmailSent,
            DevOtpCode: isRealEmailSent ? null : otpCode
        );
    }

    public Task<bool> VerifyRegistrationOtpAsync(
        VerifyRegistrationOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.EmailOrPhone) || string.IsNullOrWhiteSpace(request.OtpCode))
        {
            throw new ArgumentException("Email và mã OTP không được để trống.");
        }

        var otpKey = request.EmailOrPhone.Contains('@')
            ? request.EmailOrPhone.Trim().ToLowerInvariant()
            : NormalizePhoneKey(request.EmailOrPhone);

        if (!RegistrationOtpStore.TryGetValue(otpKey, out var entry))
        {
            throw new ArgumentException("Mã xác thực OTP không tồn tại hoặc đã hết hạn. Vui lòng bấm gửi lại mã.");
        }

        if (entry.ExpiresAtUtc < DateTime.UtcNow)
        {
            RegistrationOtpStore.TryRemove(otpKey, out _);
            throw new ArgumentException("Mã xác thực OTP đã hết hạn (quá 5 phút). Vui lòng yêu cầu gửi lại mã mới.");
        }

        var cleanOtp = request.OtpCode.Trim();
        if (!string.Equals(entry.OtpCode, cleanOtp, StringComparison.Ordinal))
        {
            var nextFailures = entry.FailedAttempts + 1;
            if (nextFailures >= MaxOtpFailedAttempts)
            {
                RegistrationOtpStore.TryRemove(otpKey, out _);
                throw new ArgumentException("Bạn đã nhập sai mã OTP quá 5 lần. Vui lòng yêu cầu gửi lại mã mới.");
            }

            RegistrationOtpStore[otpKey] = entry with { FailedAttempts = nextFailures };
            throw new ArgumentException($"Mã xác thực Email OTP không chính xác (còn {MaxOtpFailedAttempts - nextFailures} lần thử).");
        }

        return Task.FromResult(true);
    }

    private static string ResolveRegistrationOtpKey(string? email, string? phone)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim().ToLowerInvariant();
        }
        return NormalizePhoneKey(phone ?? string.Empty);
    }

    private static string NormalizePhoneKey(string rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone)) return string.Empty;
        var digits = Regex.Replace(rawPhone.Trim(), @"[^\d]", "");
        if (digits.StartsWith("84") && digits.Length >= 10)
        {
            return "0" + digits[2..];
        }
        return digits;
    }

    private static string MaskEmailDisplay(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var clean = email.Trim();
        var atIndex = clean.IndexOf('@');
        if (atIndex <= 1) return clean;
        var local = clean[..atIndex];
        var domain = clean[atIndex..];
        if (local.Length <= 2)
        {
            return $"{local[0]}***{domain}";
        }
        return $"{local[..2]}***{local[^1]}{domain}";
    }
}
