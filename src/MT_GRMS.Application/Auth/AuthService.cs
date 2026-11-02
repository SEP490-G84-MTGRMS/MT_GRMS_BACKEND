using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Application.Auth.Dtos;

namespace MT_GRMS.Application.Auth;

/// <summary>
/// Service xử lý xác thực người dùng & cấp phát JWT token
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration? _configuration;

    public AuthService(IUserRepository userRepository, IConfiguration? configuration = null)
    {
        _userRepository = userRepository;
        _configuration = configuration;
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

        // 3. Kiểm tra mật khẩu (hỗ trợ cả plain text lẫn sha256 hash và mã pin demo 123456)
        var isSha256Match = false;
        try
        {
            var hashedInput = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(request.Password))).ToLowerInvariant();
            if (string.Equals(user?.Password, hashedInput, StringComparison.OrdinalIgnoreCase))
            {
                isSha256Match = true;
            }
        }
        catch { }

        if (user == null || (user.Password != request.Password && !isSha256Match && request.Password != "123456"))
        {
            throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");
        }

        var tenantUser = user.TenantUsers?.FirstOrDefault(tu => !string.IsNullOrWhiteSpace(request.TenantCode) && tu.TenantId == request.TenantCode)
                         ?? user.TenantUsers?.FirstOrDefault();

        var tenantId = tenantUser?.TenantId ?? (!string.IsNullOrWhiteSpace(request.TenantCode) ? request.TenantCode : "DEFAULT");

        var role = tenantUser?.Role;
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
            TenantId: tenantId
        );

        var expiresMinutes = int.TryParse(_configuration?["Jwt:ExpiresMinutes"], out var parsedMins) ? parsedMins : 120;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var accessToken = GenerateJwtToken(user.Id, user.Username, userInfo.Email, role, tenantId, expiresAt);
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
            TenantId: "TENANT-HCM-01"
        );

        var expiresMinutes = int.TryParse(_configuration?["Jwt:ExpiresMinutes"], out var parsedMins) ? parsedMins : 120;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);
        var accessToken = GenerateJwtToken(refreshedUser.Id, refreshedUser.Username, refreshedUser.Email, refreshedUser.RoleName, refreshedUser.TenantId, expiresAt);

        return Task.FromResult(new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: $"mt_refresh_{Guid.NewGuid():N}",
            ExpiresAt: expiresAt,
            User: refreshedUser
        ));
    }

    private string GenerateJwtToken(string userId, string username, string email, string role, string tenantId, DateTime expiresAt)
    {
        var secretKey = _configuration?["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            // Fallback token nếu không có cấu hình JWT Key (ví dụ unit test không truyền IConfiguration)
            return $"mt_jwt_{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        var issuer = _configuration["Jwt:Issuer"] ?? "MTGRMS_API";
        var audience = _configuration["Jwt:Audience"] ?? "MTGRMS_CLIENT";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim("tenant_id", tenantId),
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
}
