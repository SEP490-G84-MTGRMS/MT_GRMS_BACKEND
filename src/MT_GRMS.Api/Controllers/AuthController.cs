using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Đăng nhập hệ thống & cấp AccessToken + RefreshToken
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
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
            return Ok(ApiResponse<LoginResponse>.Ok(result, "Đăng nhập thành công.", StatusCodes.Status200OK));
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
    /// Đăng ký tài khoản chủ cửa hàng và khởi tạo gian hàng mới
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
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
            return Ok(ApiResponse<LoginResponse>.Ok(result, "Đăng ký tài khoản và cửa hàng thành công.", StatusCodes.Status200OK));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", ex.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>
    /// Cấp mới AccessToken thông qua RefreshToken
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<object>.Fail("BAD_REQUEST", "RefreshToken không được để trống.", StatusCodes.Status400BadRequest));
        }

        var result = await _authService.RefreshTokenAsync(request, cancellationToken);
        return Ok(ApiResponse<LoginResponse>.Ok(result, "Làm mới token thành công.", StatusCodes.Status200OK));
    }

    /// <summary>
    /// Đăng xuất khỏi hệ thống
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public IActionResult Logout()
    {
        return Ok(ApiResponse<string>.Ok(string.Empty, "Đăng xuất thành công.", StatusCodes.Status200OK));
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
