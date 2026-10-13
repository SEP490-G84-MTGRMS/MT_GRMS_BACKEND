using MT_GRMS.Application.Auth.Dtos;

namespace MT_GRMS.Application.Auth;

/// <summary>
/// Service xử lý xác thực người dùng & cấp phát JWT token
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;

    public AuthService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
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

        // 3. Nếu không tìm thấy user HOẶC sai mật khẩu -> Từ chối xác thực (HTTP 401)
        if (user == null || user.Password != request.Password)
        {
            throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");
        }

        // 4. Đăng nhập thành công với thông tin user từ database
        var userInfo = new UserInfo(
            Id: user.Id,
            Username: user.Username,
            FullName: user.Username,
            Email: $"{user.Username}@mtgrms.vn",
            RoleName: "ADMIN",
            BranchId: null,
            TenantId: request.TenantCode ?? "DEFAULT"
        );

        return new LoginResponse(
            AccessToken: $"mt_jwt_{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            RefreshToken: $"mt_refresh_{Guid.NewGuid():N}",
            ExpiresAt: DateTime.UtcNow.AddHours(2),
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

        return Task.FromResult(new LoginResponse(
            AccessToken: $"mt_jwt_{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            RefreshToken: $"mt_refresh_{Guid.NewGuid():N}",
            ExpiresAt: DateTime.UtcNow.AddHours(2),
            User: refreshedUser
        ));
    }
}
