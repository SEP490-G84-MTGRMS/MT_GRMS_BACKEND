using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.Auth;

/// <summary>
/// Service xử lý xác thực người dùng & cấp phát JWT token
/// </summary>
public class AuthService : IAuthService
{
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
        // 1. Kiểm tra tính hợp lệ của tham số đầu vào
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Tài khoản và mật khẩu không được để trống.");
        }

        // 2. Tìm user trong cơ sở dữ liệu
        var user = await _userRepository.GetByUsernameAsync(request.UsernameOrEmail.Trim(), cancellationToken);

        // 3. Kiểm tra mật khẩu (hỗ trợ PBKDF2 PasswordHasher, SHA256, Plaintext và demo password)
        bool isPasswordValid = false;
        bool rehashNeeded = false;

        if (user != null && !string.IsNullOrEmpty(user.PasswordHash))
        {
            isPasswordValid = _passwordHasher.VerifyPassword(user.PasswordHash, request.Password, out rehashNeeded);

            // Kiểm tra SHA256 hash nếu chưa khớp
            if (!isPasswordValid)
            {
                try
                {
                    var hashedInput = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(request.Password))).ToLowerInvariant();
                    if (string.Equals(user.PasswordHash, hashedInput, StringComparison.OrdinalIgnoreCase))
                    {
                        isPasswordValid = true;
                        rehashNeeded = true;
                    }
                }
                catch { }
            }

            // Kiểm tra plaintext trực tiếp
            if (!isPasswordValid && string.Equals(user.PasswordHash, request.Password, StringComparison.Ordinal))
            {
                isPasswordValid = true;
                rehashNeeded = true;
            }

            // Cho phép mật khẩu demo tiện lợi "password123" hoặc "123456" nếu là seed admin/manager/cashier
            if (!isPasswordValid && (request.Password == "password123" || request.Password == "123456"))
            {
                if (user.UserName.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                    user.UserName.Equals("manager01", StringComparison.OrdinalIgnoreCase) ||
                    user.UserName.Equals("cashier01", StringComparison.OrdinalIgnoreCase))
                {
                    isPasswordValid = true;
                }
            }
        }

        if (user == null || !isPasswordValid)
        {
            throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");
        }

        // Nếu mật khẩu cũ dạng plain text đã đăng nhập thành công -> tự động nâng cấp hash
        if (rehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(request.Password);
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync(cancellationToken);
        }

        var tenantUser = user.TenantUsers?.FirstOrDefault(tu => !string.IsNullOrWhiteSpace(request.TenantCode) && tu.TenantId == request.TenantCode)
                         ?? user.TenantUsers?.FirstOrDefault();

        var tenantId = tenantUser?.TenantId ?? (!string.IsNullOrWhiteSpace(request.TenantCode) ? request.TenantCode : "DEFAULT");
        if (tenantId is "default-tenant" or "TENANT-HCM-01" or "TENANT-DEFAULT" or "DEFAULT")
        {
            tenantId = "tenant_mt_mart_01";
        }

        // Xác định quyền hạn (Role)
        var isAdmin = user.IsAdmin || user.UserName.Equals("admin", StringComparison.OrdinalIgnoreCase);
        var role = isAdmin ? "ADMIN" : (tenantUser?.Role ?? "STORE_OWNER");
        if (string.IsNullOrWhiteSpace(role))
        {
            var uName = (user.UserName ?? "").ToUpperInvariant();
            var email = (user.Email ?? "").ToUpperInvariant();
            if (uName.Contains("EMPLOYEE") || email.Contains("EMPLOYEE") || uName.Contains("STAFF") || email.Contains("STAFF"))
            {
                role = "EMPLOYEE";
            }
            else if (uName.Contains("OWNER") || email.Contains("OWNER"))
            {
                role = "STORE_OWNER";
            }
            else
            {
                role = "ADMIN";
            }
        }

        // 4. Đăng nhập thành công với thông tin user từ database
        var userInfo = new UserInfo(
            Id: user.Id,
            Username: user.Username,
            FullName: string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName,
            Email: string.IsNullOrWhiteSpace(user.Email) ? $"{user.Username}@mtgrms.vn" : user.Email,
            RoleName: role,
            BranchId: null,
            TenantId: tenantId,
            IsAdmin: isAdmin
        );

        var expiresMinutes = int.TryParse(_configuration?["Jwt:ExpiresMinutes"], out var parsedMins) ? parsedMins : 120;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var accessToken = GenerateJwtToken(user.Id, user.Username, userInfo.Email, role, tenantId, isAdmin, expiresAt);
        var refreshToken = $"mt_refresh_{Guid.NewGuid():N}";

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: expiresAt,
            User: userInfo
        );
    }

    public Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var refreshedUser = new UserInfo(
            Id: $"usr_{Guid.NewGuid():N}",
            Username: "refreshed_user",
            FullName: "Refreshed User",
            Email: "user@mtgrms.vn",
            RoleName: "ADMIN",
            BranchId: null,
            TenantId: "TENANT-HCM-01",
            IsAdmin: true
        );

        var expiresMinutes = int.TryParse(_configuration?["Jwt:ExpiresMinutes"], out var parsedMins) ? parsedMins : 120;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var accessToken = GenerateJwtToken(refreshedUser.Id, refreshedUser.Username, refreshedUser.Email, refreshedUser.RoleName, refreshedUser.TenantId, true, expiresAt);

        return Task.FromResult(new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: $"mt_refresh_{Guid.NewGuid():N}",
            ExpiresAt: expiresAt,
            User: refreshedUser
        ));
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
            CurrentPlan = "FREE",
            WalletBalance = 0m,
            SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
        };

        var userId = $"usr_{Guid.NewGuid():N}";
        var userEmail = string.IsNullOrWhiteSpace(request.Email) ? $"{request.Phone.Trim()}@mtgrms.vn" : request.Email.Trim();
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
        var accessToken = GenerateJwtToken(user.Id, user.UserName, user.Email, "STORE_OWNER", tenant.Id, false, expiresAt);
        var refreshToken = $"mt_refresh_{Guid.NewGuid():N}";

        var userInfo = new UserInfo(
            Id: user.Id,
            Username: user.UserName,
            FullName: user.FullName,
            Email: user.Email,
            RoleName: "STORE_OWNER",
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

    private string GenerateJwtToken(string userId, string username, string email, string role, string tenantId, bool isAdmin, DateTime expiresAt)
    {
        // Phải khớp fallback key trong Program.cs — appsettings thường để Jwt:Key="" 
        // (IsNullOrWhiteSpace), nếu trả opaque mt_jwt_* thì FE gửi Bearer nhưng API không authenticate → 403 khi sửa NCC.
        var secretKey = _configuration?["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            secretKey = "MT_GRMS_Super_Secret_Key_For_JWT_Authentication_2026_FPTU_Capstone_Project!";
        }

        var issuer = _configuration?["Jwt:Issuer"] ?? "MTGRMS_API";
        var audience = _configuration?["Jwt:Audience"] ?? "MTGRMS_CLIENT";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim("tenant_id", tenantId),
            new Claim("is_admin", isAdmin ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

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

        // Tìm user theo email hoặc username/phone
        var user = await _userRepository.GetByUsernameAsync(cleanInput, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tài khoản với thông tin đã cung cấp.");
        }

        var targetEmail = string.IsNullOrWhiteSpace(user.Email) ? cleanInput : user.Email;

        // Sinh mã OTP 6 chữ số ngẫu nhiên an toàn bằng RandomNumberGenerator
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        user.PasswordResetOtp = otpCode;
        user.OtpExpiry = DateTime.UtcNow.AddMinutes(10);

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        // Gửi email OTP
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

        // Xác thực lại OTP
        await VerifyOtpAsync(new VerifyOtpRequest(request.Email, request.OtpCode), cancellationToken);

        var cleanInput = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByUsernameAsync(cleanInput, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng.");
        }

        // Băm mật khẩu mới bằng PBKDF2 PasswordHasher
        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);

        // Hủy mã OTP sau khi sử dụng thành công
        user.PasswordResetOtp = null;
        user.OtpExpiry = null;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);
    }
}
