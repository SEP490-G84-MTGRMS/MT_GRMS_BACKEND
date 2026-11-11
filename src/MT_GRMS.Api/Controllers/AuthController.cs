using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/v1/[controller]")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Đăng nhập hệ thống & cấp AccessToken + RefreshToken qua HttpOnly Cookie
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<UserInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "Tài khoản và mật khẩu không được để trống.", StatusCodes.Status400BadRequest));
        }

        try
        {
            var result = await _authService.LoginAsync(request, cancellationToken);
            SetAuthCookies(result.AccessToken, result.RefreshToken, result.ExpiresAt);
            return Ok(ApiResponse<UserInfo>.Ok(result.User, "Đăng nhập thành công.", StatusCodes.Status200OK));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", ex.Message, StatusCodes.Status401Unauthorized));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Kiểm tra trùng số điện thoại và khởi tạo phiên xác thực SMS OTP (TextBee Android Gateway / Dev Fallback)
    /// </summary>
    [HttpPost("register/send-otp")]
    [ProducesResponseType(typeof(ApiResponse<SendRegistrationOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendRegistrationOtp([FromBody] SendRegistrationOtpRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "Vui lòng nhập số điện thoại để nhận mã xác thực SMS.", StatusCodes.Status400BadRequest));
        }

        try
        {
            var result = await _authService.SendRegistrationOtpAsync(request, cancellationToken);
            return Ok(ApiResponse<SendRegistrationOtpResponse>.Ok(result, result.Message, StatusCodes.Status200OK));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Kiểm tra mã SMS OTP đăng ký cửa hàng
    /// </summary>
    [HttpPost("register/verify-otp")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyRegistrationOtp([FromBody] VerifyRegistrationOtpRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.OtpCode))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "Số điện thoại và mã OTP không được để trống.", StatusCodes.Status400BadRequest));
        }

        try
        {
            await _authService.VerifyRegistrationOtpAsync(request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "Xác thực số điện thoại thành công.", StatusCodes.Status200OK));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Đăng ký tài khoản chủ cửa hàng và khởi tạo gian hàng mới
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<UserInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterTenantRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "Vui lòng nhập đầy đủ họ tên, số điện thoại và mật khẩu.", StatusCodes.Status400BadRequest));
        }

        try
        {
            var result = await _authService.RegisterTenantAsync(request, cancellationToken);
            SetAuthCookies(result.AccessToken, result.RefreshToken, result.ExpiresAt);
            return Ok(ApiResponse<UserInfo>.Ok(result.User, "Đăng ký tài khoản và cửa hàng thành công.", StatusCodes.Status200OK));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Cấp mới AccessToken thông qua RefreshToken Cookie (Token Rotation)
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<UserInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshTokenRequest? request, CancellationToken cancellationToken)
    {
        // 1. Ưu tiên đọc RefreshToken từ HttpOnly Cookie
        string? refreshToken = null;
        try
        {
            refreshToken = Request?.Cookies?["refresh_token"];
        }
        catch { }

        if (string.IsNullOrWhiteSpace(refreshToken) && request != null)
        {
            refreshToken = request.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "RefreshToken không tồn tại hoặc đã hết hạn.", StatusCodes.Status401Unauthorized));
        }

        try
        {
            var result = await _authService.RefreshTokenAsync(new RefreshTokenRequest(refreshToken), cancellationToken);
            SetAuthCookies(result.AccessToken, result.RefreshToken, result.ExpiresAt);
            return Ok(ApiResponse<UserInfo>.Ok(result.User, "Làm mới phiên thành công.", StatusCodes.Status200OK));
        }
        catch (UnauthorizedAccessException ex)
        {
            DeleteAuthCookies();
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", ex.Message, StatusCodes.Status401Unauthorized));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Lấy thông tin tài khoản người dùng hiện tại từ HttpOnly Cookie
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User?.FindFirst("sub")?.Value
                     ?? User?.FindFirst("userId")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Chưa xác thực danh tính.", StatusCodes.Status401Unauthorized));
        }

        var user = await _authService.GetCurrentUserAsync(userId, cancellationToken);
        if (user == null)
        {
            DeleteAuthCookies();
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Người dùng không tồn tại hoặc đã bị vô hiệu hóa.", StatusCodes.Status401Unauthorized));
        }

        return Ok(ApiResponse<UserInfo>.Ok(user, "Lấy thông tin người dùng thành công.", StatusCodes.Status200OK));
    }

    /// <summary>
    /// Đăng xuất khỏi hệ thống & xóa HttpOnly Cookies
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        var userId = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await _authService.RevokeRefreshTokenAsync(userId, cancellationToken);
        }

        DeleteAuthCookies();
        return Ok(ApiResponse<object>.Ok(new { success = true }, "Đăng xuất thành công.", StatusCodes.Status200OK));
    }

    private void SetAuthCookies(string accessToken, string refreshToken, DateTime expiresAt)
    {
        if (Response?.Cookies == null) return;
        var isHttps = Request?.IsHttps ?? false;

        var accessCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
            IsEssential = true
        };
        Response.Cookies.Append("access_token", accessToken, accessCookieOptions);

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = DateTime.UtcNow.AddDays(7),
            IsEssential = true
        };
        Response.Cookies.Append("refresh_token", refreshToken, refreshCookieOptions);
    }

    private void DeleteAuthCookies()
    {
        if (Response?.Cookies == null) return;
        Response.Cookies.Delete("access_token", new CookieOptions { Path = "/" });
        Response.Cookies.Delete("refresh_token", new CookieOptions { Path = "/api/auth" });
    }

    /// <summary>
    /// Gửi mã xác thực OTP khôi phục mật khẩu qua Email
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "Vui lòng nhập địa chỉ email hợp lệ.", StatusCodes.Status400BadRequest));
        }

        try
        {
            await _authService.ForgotPasswordAsync(request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "Mã xác thực OTP đã được gửi đến email của bạn.", StatusCodes.Status200OK));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Xác thực mã OTP khôi phục mật khẩu
    /// </summary>
    [HttpPost("verify-otp")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.OtpCode))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "Email và mã OTP không được để trống.", StatusCodes.Status400BadRequest));
        }

        try
        {
            await _authService.VerifyOtpAsync(request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "Mã xác thực hợp lệ.", StatusCodes.Status200OK));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Đặt lại mật khẩu mới với mã OTP hợp lệ
    /// </summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.OtpCode) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "Vui lòng điền đầy đủ email, mã OTP và mật khẩu mới.", StatusCodes.Status400BadRequest));
        }

        try
        {
            await _authService.ResetPasswordAsync(request, cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { success = true }, "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.", StatusCodes.Status200OK));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", ex.Message, StatusCodes.Status404NotFound));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }
}
