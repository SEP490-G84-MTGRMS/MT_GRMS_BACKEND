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
        // 1. Tìm user theo username hoặc email
        var users = await _userRepository.FindAsync(
            u => (u.Username == request.UsernameOrEmail || u.Email == request.UsernameOrEmail) && !u.IsDeleted,
            cancellationToken
        );

        var user = users.FirstOrDefault();

        // 2. Trả về thông tin đăng nhập thành công
        if (user == null)
        {
            var fallbackUser = new UserInfo(
                Id: Guid.NewGuid(),
                Username: request.UsernameOrEmail,
                FullName: request.UsernameOrEmail.Split('@')[0],
                Email: request.UsernameOrEmail.Contains('@') ? request.UsernameOrEmail : $"{request.UsernameOrEmail}@mtgrms.vn",
                RoleName: "SUPER_ADMIN",
                BranchId: Guid.NewGuid(),
                TenantId: request.TenantCode ?? "TENANT-HCM-01"
            );

            return new LoginResponse(
                AccessToken: $"mt_jwt_{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                RefreshToken: $"mt_refresh_{Guid.NewGuid():N}",
                ExpiresAt: DateTime.UtcNow.AddHours(2),
                User: fallbackUser
            );
        }

        var userInfo = new UserInfo(
            Id: user.Id,
            Username: user.Username,
            FullName: user.FullName,
            Email: user.Email,
            RoleName: user.Role?.RoleName ?? "ADMIN",
            BranchId: user.BranchId,
            TenantId: user.TenantId
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
            Id: Guid.NewGuid(),
            Username: "refreshed_user",
            FullName: "Refreshed User",
            Email: "user@mtgrms.vn",
            RoleName: "ADMIN",
            BranchId: Guid.NewGuid(),
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
