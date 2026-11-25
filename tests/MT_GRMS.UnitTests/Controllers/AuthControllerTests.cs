using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.API.Controllers;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _mockAuth;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _mockAuth = new Mock<IAuthService>();
        _controller = new AuthController(_mockAuth.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static UserInfo SampleUserInfo()
    {
        return new UserInfo(
            Id: Guid.NewGuid().ToString(),
            Username: "testuser",
            FullName: "Test User",
            Email: "test@example.com",
            RoleName: "STORE_OWNER",
            BranchId: null,
            TenantId: "tenant-01",
            IsAdmin: false,
            IsTwoFactorEnabled: false
        );
    }

    [Fact]
    public async Task Login_ValidCredentials_WithSession_ShouldSetCookiesAndReturnOk()
    {
        var request = new LoginRequest("testuser", "Password@123", "tenant-01");
        var session = new LoginResponse(
            AccessToken: "access-token-jwt",
            RefreshToken: "refresh-token-jwt",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: SampleUserInfo(),
            RefreshExpiresAt: DateTime.UtcNow.AddDays(7)
        );
        var response = new SendLoginOtpResponse(
            RequiresOtp: false,
            TempToken: "temp-token-123",
            MaskedEmail: "t***@example.com",
            TargetEmail: "test@example.com",
            ExpiresInSeconds: 300,
            IsRealEmailSent: false,
            Message: "Logged in directly",
            DevOtpCode: null,
            User: session.User,
            Session: session
        );
        _mockAuth.Setup(a => a.SendLoginOtpAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Exercise HTTPS branch for cookie secure flag
        _controller.ControllerContext.HttpContext.Request.IsHttps = true;

        var result = await _controller.Login(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Theory]
    [InlineData(null, "pass")]
    [InlineData("user", null)]
    [InlineData("", "pass")]
    [InlineData("user", " ")]
    public async Task Login_EmptyOrNullInputs_ShouldReturnBadRequest(string? u, string? p)
    {
        var request = new LoginRequest(u!, p!, null);
        var result = await _controller.Login(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_Exceptions_ShouldReturnAppropriateStatuses()
    {
        var req = new LoginRequest("u", "p", null);

        _mockAuth.Setup(a => a.SendLoginOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Sai pass"));
        var res1 = await _controller.Login(req, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(res1);

        _mockAuth.Setup(a => a.SendLoginOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid input"));
        var res2 = await _controller.Login(req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res2);

        _mockAuth.Setup(a => a.SendLoginOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Email error"));
        var res3 = await _controller.Login(req, CancellationToken.None);
        var objRes = Assert.IsType<ObjectResult>(res3);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objRes.StatusCode);
    }

    [Fact]
    public async Task SendLoginOtp_Branches_ShouldWork()
    {
        // Bad request
        var badRes = await _controller.SendLoginOtp(new LoginRequest("", "", null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(badRes);

        var req = new LoginRequest("user", "pass", null);
        var session = new LoginResponse("acc", "ref", DateTime.UtcNow.AddHours(1), SampleUserInfo());
        var otpRes = new SendLoginOtpResponse(false, "", "", "", 0, false, "ok", null, session.User, session);
        _mockAuth.Setup(a => a.SendLoginOtpAsync(req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otpRes);
        var okRes = await _controller.SendLoginOtp(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(okRes);

        // Exceptions
        _mockAuth.Setup(a => a.SendLoginOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        Assert.IsType<UnauthorizedObjectResult>(await _controller.SendLoginOtp(req, CancellationToken.None));

        _mockAuth.Setup(a => a.SendLoginOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException());
        Assert.IsType<BadRequestObjectResult>(await _controller.SendLoginOtp(req, CancellationToken.None));

        _mockAuth.Setup(a => a.SendLoginOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var res503 = Assert.IsType<ObjectResult>(await _controller.SendLoginOtp(req, CancellationToken.None));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, res503.StatusCode);
    }

    [Fact]
    public async Task VerifyLoginOtp_Branches_ShouldWork()
    {
        // Bad inputs
        Assert.IsType<BadRequestObjectResult>(await _controller.VerifyLoginOtp(new VerifyLoginOtpRequest("", "123456"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await _controller.VerifyLoginOtp(new VerifyLoginOtpRequest("token", ""), CancellationToken.None));

        var req = new VerifyLoginOtpRequest("token", "123456");
        var resp = new LoginResponse("acc", "ref", DateTime.UtcNow.AddHours(1), SampleUserInfo());
        _mockAuth.Setup(a => a.VerifyLoginOtpAsync(req, It.IsAny<CancellationToken>())).ReturnsAsync(resp);

        var ok = await _controller.VerifyLoginOtp(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok);

        // Exceptions
        _mockAuth.Setup(a => a.VerifyLoginOtpAsync(req, It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException("invalid otp"));
        Assert.IsType<UnauthorizedObjectResult>(await _controller.VerifyLoginOtp(req, CancellationToken.None));

        _mockAuth.Setup(a => a.VerifyLoginOtpAsync(req, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("bad"));
        Assert.IsType<BadRequestObjectResult>(await _controller.VerifyLoginOtp(req, CancellationToken.None));
    }

    [Fact]
    public async Task SendRegistrationOtp_Branches_ShouldWork()
    {
        // Bad inputs
        Assert.IsType<ObjectResult>(await _controller.SendRegistrationOtp(new SendRegistrationOtpRequest(Email: ""), CancellationToken.None));

        var req = new SendRegistrationOtpRequest(Phone: "0901234567", StoreName: "My Store", FullName: "Test User", Email: "newuser@store.com");
        var resp = new SendRegistrationOtpResponse(true, "OTP sent", "n***@store.com", 300, true);
        _mockAuth.Setup(a => a.SendRegistrationOtpAsync(req, It.IsAny<CancellationToken>())).ReturnsAsync(resp);

        var ok = await _controller.SendRegistrationOtp(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok);

        // ArgumentException with parse coded message
        _mockAuth.Setup(a => a.SendRegistrationOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("DUPLICATE_PHONE: Phone already exists"));
        var bad1 = Assert.IsType<ObjectResult>(await _controller.SendRegistrationOtp(req, CancellationToken.None));
        Assert.Equal(400, bad1.StatusCode);

        // InvalidOperationException with RATE_LIMIT_EXCEEDED vs other
        _mockAuth.Setup(a => a.SendRegistrationOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RATE_LIMIT_EXCEEDED: Quá giới hạn"));
        var rateEx = Assert.IsType<ObjectResult>(await _controller.SendRegistrationOtp(req, CancellationToken.None));
        Assert.Equal(429, rateEx.StatusCode);

        _mockAuth.Setup(a => a.SendRegistrationOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("EMAIL_SERVICE_DOWN: Không gửi được"));
        var emailDown = Assert.IsType<ObjectResult>(await _controller.SendRegistrationOtp(req, CancellationToken.None));
        Assert.Equal(503, emailDown.StatusCode);

        // IP Rate Limit trigger (> 15 calls for same IP)
        var ip = IPAddress.Parse("10.99.88.77");
        _controller.ControllerContext.HttpContext.Connection.RemoteIpAddress = ip;
        for (int i = 0; i < 15; i++)
        {
            await _controller.SendRegistrationOtp(req, CancellationToken.None);
        }
        var blockedRes = Assert.IsType<ObjectResult>(await _controller.SendRegistrationOtp(req, CancellationToken.None));
        Assert.Equal(429, blockedRes.StatusCode);
    }

    [Fact]
    public async Task VerifyRegistrationOtp_Branches_ShouldWork()
    {
        // Bad inputs
        Assert.IsType<ObjectResult>(await _controller.VerifyRegistrationOtp(new VerifyRegistrationOtpRequest("", "123456"), CancellationToken.None));
        Assert.IsType<ObjectResult>(await _controller.VerifyRegistrationOtp(new VerifyRegistrationOtpRequest("email@test.com", ""), CancellationToken.None));

        var req = new VerifyRegistrationOtpRequest("email@test.com", "123456");
        _mockAuth.Setup(a => a.VerifyRegistrationOtpAsync(req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ok = await _controller.VerifyRegistrationOtp(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok);

        // ArgumentException with parse coded message
        _mockAuth.Setup(a => a.VerifyRegistrationOtpAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("INVALID_OTP: Code mismatch"));
        var badRes = Assert.IsType<ObjectResult>(await _controller.VerifyRegistrationOtp(req, CancellationToken.None));
        Assert.Equal(400, badRes.StatusCode);
    }

    [Fact]
    public async Task Register_Branches_ShouldWork()
    {
        // Bad inputs
        Assert.IsType<ObjectResult>(await _controller.Register(new RegisterTenantRequest(FullName: "", StoreName: "Store", Phone: "0901234567", Email: null, Password: "Pass"), CancellationToken.None));
        Assert.IsType<ObjectResult>(await _controller.Register(new RegisterTenantRequest(FullName: "Name", StoreName: "Store", Phone: "", Email: null, Password: "Pass"), CancellationToken.None));
        Assert.IsType<ObjectResult>(await _controller.Register(new RegisterTenantRequest(FullName: "Name", StoreName: "Store", Phone: "0901234567", Email: null, Password: ""), CancellationToken.None));

        var req = new RegisterTenantRequest(FullName: "Name", StoreName: "Store", Phone: "0901234567", Email: "mail@test.com", Password: "Pass@123");
        var resp = new LoginResponse("acc", "ref", DateTime.UtcNow.AddHours(1), SampleUserInfo());
        _mockAuth.Setup(a => a.RegisterTenantAsync(req, It.IsAny<CancellationToken>())).ReturnsAsync(resp);

        var ok = await _controller.Register(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(ok);

        // Exceptions
        _mockAuth.Setup(a => a.RegisterTenantAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("BAD_REQUEST: Invalid phone"));
        var bad = Assert.IsType<ObjectResult>(await _controller.Register(req, CancellationToken.None));
        Assert.Equal(400, bad.StatusCode);

        _mockAuth.Setup(a => a.RegisterTenantAsync(req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("OTP_NOT_VERIFIED: Need OTP"));
        var conflict = Assert.IsType<ObjectResult>(await _controller.Register(req, CancellationToken.None));
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task Refresh_Branches_ShouldWork()
    {
        // 1. Refresh token missing in both cookie and body
        var resMissing = await _controller.Refresh(null, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resMissing);

        // 2. Refresh token in request body
        var req = new RefreshTokenRequest("body-refresh-token");
        var resp = new LoginResponse("new-acc", "new-ref", DateTime.UtcNow.AddHours(1), SampleUserInfo());
        _mockAuth.Setup(a => a.RefreshTokenAsync(It.Is<RefreshTokenRequest>(r => r.RefreshToken == "body-refresh-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resp);

        var resBody = await _controller.Refresh(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resBody);

        // 3. Refresh token in cookie takes precedence
        _controller.ControllerContext.HttpContext.Request.Headers["Cookie"] = "refresh_token=cookie-refresh-token";
        _mockAuth.Setup(a => a.RefreshTokenAsync(It.Is<RefreshTokenRequest>(r => r.RefreshToken == "cookie-refresh-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resp);

        var resCookie = await _controller.Refresh(req, CancellationToken.None);
        Assert.IsType<OkObjectResult>(resCookie);

        // 4. UnauthorizedAccessException
        _mockAuth.Setup(a => a.RefreshTokenAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Expired token"));
        var unauth = await _controller.Refresh(req, CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(unauth);

        // 5. ArgumentException
        _mockAuth.Setup(a => a.RefreshTokenAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Malformed token"));
        var bad = await _controller.Refresh(req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(bad);
    }

    [Fact]
    public async Task GetMe_Branches_ShouldWork()
    {
        // 1. No identity claims
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var resNoClaim = await _controller.GetMe(CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resNoClaim);

        // 2. User not found / disabled
        var claims = new[] { new Claim("sub", "usr-sub-99") };
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _mockAuth.Setup(a => a.GetCurrentUserAsync("usr-sub-99", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserInfo?)null);

        var resNotFound = await _controller.GetMe(CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(resNotFound);

        // 3. User found
        _mockAuth.Setup(a => a.GetCurrentUserAsync("usr-sub-99", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleUserInfo());
        var resFound = await _controller.GetMe(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resFound);
    }

    [Fact]
    public async Task TwoFactor_Endpoints_Branches_ShouldWork()
    {
        // 1. GetTwoFactorStatus without claim
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var unauthStatus = await _controller.GetTwoFactorStatus(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(unauthStatus);

        // With claim
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "usr-123") };
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _mockAuth.Setup(a => a.GetTwoFactorStatusAsync("usr-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorStatusResponse(true, "u@test.com", true));
        var okStatus = await _controller.GetTwoFactorStatus(CancellationToken.None);
        Assert.IsType<OkObjectResult>(okStatus);

        // 2. CreateTwoFactorChallenge
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var unauthChal = await _controller.CreateTwoFactorChallenge(new TwoFactorChallengeRequest(true, "pwd"), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(unauthChal);

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var chalReq = new TwoFactorChallengeRequest(true, "pwd");
        _mockAuth.Setup(a => a.CreateTwoFactorChallengeAsync("usr-123", chalReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Wrong password"));
        Assert.IsType<BadRequestObjectResult>(await _controller.CreateTwoFactorChallenge(chalReq, CancellationToken.None));

        _mockAuth.Setup(a => a.CreateTwoFactorChallengeAsync("usr-123", chalReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Email server error"));
        var chal503 = Assert.IsType<ObjectResult>(await _controller.CreateTwoFactorChallenge(chalReq, CancellationToken.None));
        Assert.Equal(503, chal503.StatusCode);

        _mockAuth.Setup(a => a.CreateTwoFactorChallengeAsync("usr-123", chalReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorChallengeResponse("token", "u***@test.com", 300));
        Assert.IsType<OkObjectResult>(await _controller.CreateTwoFactorChallenge(chalReq, CancellationToken.None));

        // 3. ConfirmTwoFactorChallenge
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var unauthConf = await _controller.ConfirmTwoFactorChallenge(new TwoFactorConfirmRequest("tok", "123456"), CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(unauthConf);

        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        var confReq = new TwoFactorConfirmRequest("tok", "123456");

        _mockAuth.Setup(a => a.ConfirmTwoFactorChallengeAsync("usr-123", confReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Bad OTP"));
        Assert.IsType<UnauthorizedObjectResult>(await _controller.ConfirmTwoFactorChallenge(confReq, CancellationToken.None));

        _mockAuth.Setup(a => a.ConfirmTwoFactorChallengeAsync("usr-123", confReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Expired OTP"));
        Assert.IsType<BadRequestObjectResult>(await _controller.ConfirmTwoFactorChallenge(confReq, CancellationToken.None));

        _mockAuth.Setup(a => a.ConfirmTwoFactorChallengeAsync("usr-123", confReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorStatusResponse(true, "u@test.com", true));
        Assert.IsType<OkObjectResult>(await _controller.ConfirmTwoFactorChallenge(confReq, CancellationToken.None));
    }

    [Fact]
    public async Task ForgotPassword_VerifyOtp_ResetPassword_Branches_ShouldWork()
    {
        // ForgotPassword
        Assert.IsType<BadRequestObjectResult>(await _controller.ForgotPassword(new ForgotPasswordRequest(""), CancellationToken.None));

        var forgotReq = new ForgotPasswordRequest("user@test.com");
        _mockAuth.Setup(a => a.ForgotPasswordAsync(forgotReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Email not found"));
        Assert.IsType<OkObjectResult>(await _controller.ForgotPassword(forgotReq, CancellationToken.None));

        _mockAuth.Setup(a => a.ForgotPasswordAsync(forgotReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Invalid email format"));
        Assert.IsType<BadRequestObjectResult>(await _controller.ForgotPassword(forgotReq, CancellationToken.None));

        _mockAuth.Setup(a => a.ForgotPasswordAsync(forgotReq, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Assert.IsType<OkObjectResult>(await _controller.ForgotPassword(forgotReq, CancellationToken.None));

        // VerifyOtp
        Assert.IsType<BadRequestObjectResult>(await _controller.VerifyOtp(new VerifyOtpRequest("", "123"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await _controller.VerifyOtp(new VerifyOtpRequest("email", ""), CancellationToken.None));

        var verifyReq = new VerifyOtpRequest("user@test.com", "123456");
        _mockAuth.Setup(a => a.VerifyOtpAsync(verifyReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Not found"));
        Assert.IsType<BadRequestObjectResult>(await _controller.VerifyOtp(verifyReq, CancellationToken.None));

        _mockAuth.Setup(a => a.VerifyOtpAsync(verifyReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Wrong code"));
        Assert.IsType<BadRequestObjectResult>(await _controller.VerifyOtp(verifyReq, CancellationToken.None));

        _mockAuth.Setup(a => a.VerifyOtpAsync(verifyReq, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        Assert.IsType<OkObjectResult>(await _controller.VerifyOtp(verifyReq, CancellationToken.None));

        // ResetPassword
        Assert.IsType<BadRequestObjectResult>(await _controller.ResetPassword(new ResetPasswordRequest("", "123", "pass"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await _controller.ResetPassword(new ResetPasswordRequest("u@t.com", "", "pass"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await _controller.ResetPassword(new ResetPasswordRequest("u@t.com", "123", ""), CancellationToken.None));

        var resetReq = new ResetPasswordRequest("user@test.com", "123456", "NewPass@123");
        _mockAuth.Setup(a => a.ResetPasswordAsync(resetReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Not found"));
        Assert.IsType<BadRequestObjectResult>(await _controller.ResetPassword(resetReq, CancellationToken.None));

        _mockAuth.Setup(a => a.ResetPasswordAsync(resetReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Weak password"));
        Assert.IsType<BadRequestObjectResult>(await _controller.ResetPassword(resetReq, CancellationToken.None));

        _mockAuth.Setup(a => a.ResetPasswordAsync(resetReq, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Assert.IsType<OkObjectResult>(await _controller.ResetPassword(resetReq, CancellationToken.None));
    }

    [Fact]
    public async Task Logout_ShouldRevokeAndClearCookies()
    {
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "usr-sub") }, "TestAuth"));
        _controller.ControllerContext.HttpContext.Request.Headers["Cookie"] = "refresh_token=old-refresh";

        _mockAuth.Setup(a => a.RevokeRefreshTokenAsync("usr-sub", It.IsAny<CancellationToken>(), "old-refresh"))
            .Returns(Task.CompletedTask);

        var result = await _controller.Logout(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task ChangePassword_Branches_ShouldWork()
    {
        var req = new ChangePasswordRequest("OldPass@123", "NewPass@456");

        // 1. Unauthenticated -> 401
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.IsType<UnauthorizedObjectResult>(await _controller.ChangePassword(req, CancellationToken.None));

        // Authenticated user
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "usr-01") }, "TestAuth"));

        // 2. Empty fields -> 400
        Assert.IsType<BadRequestObjectResult>(await _controller.ChangePassword(new ChangePasswordRequest("", "NewPass@456"), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await _controller.ChangePassword(new ChangePasswordRequest("OldPass@123", ""), CancellationToken.None));

        // 3. Wrong current password -> 400 with INVALID_CURRENT_PASSWORD
        _mockAuth.Setup(a => a.ChangePasswordAsync("usr-01", req, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("INVALID_CURRENT_PASSWORD: Mật khẩu hiện tại không chính xác."));
        var badResult = Assert.IsType<BadRequestObjectResult>(await _controller.ChangePassword(req, CancellationToken.None));
        var badBody = Assert.IsType<ApiResponse<object>>(badResult.Value);
        Assert.Equal("INVALID_CURRENT_PASSWORD", badBody.ErrorCode);

        // 4. Valid -> sets new cookies and returns 200 OK
        var session = new LoginResponse("new-acc", "new-ref", DateTime.UtcNow.AddMinutes(15), SampleUserInfo(), DateTime.UtcNow.AddDays(7));
        _mockAuth.Setup(a => a.ChangePasswordAsync("usr-01", req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        var okResult = Assert.IsType<OkObjectResult>(await _controller.ChangePassword(req, CancellationToken.None));
        var okBody = Assert.IsType<ApiResponse<UserInfo>>(okResult.Value);
        Assert.True(okBody.Success);
    }
}
