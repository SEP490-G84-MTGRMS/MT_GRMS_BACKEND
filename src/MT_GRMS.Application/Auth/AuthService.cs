using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Auth.Validators;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Auth;

/// <summary>
/// Service xử lý xác thực người dùng, Email OTP (2FA Đăng nhập & Đăng ký) và cấp phát JWT token
/// </summary>
public class AuthService : IAuthService
{
    private const int MaxOtpFailedAttempts = 5;
    private const int MaxSendOtpPerWindow = 5;
    private static readonly TimeSpan SendOtpRateLimitWindow = TimeSpan.FromMinutes(10);

    private sealed record LoginOtpEntry(string UserId, string? TenantCode, string OtpCode, DateTime ExpiresAtUtc, int FailedAttempts = 0);
    private sealed record TwoFactorSettingsEntry(string UserId, bool Enabled, string OtpCode, DateTime ExpiresAtUtc, int FailedAttempts = 0);

    private static readonly ConcurrentDictionary<string, RegistrationOtpState> RegistrationOtpStore = new();
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> RegistrationOtpSendWindows = new();
    private static readonly object InMemoryOtpSync = new();
    private static readonly ConcurrentDictionary<string, LoginOtpEntry> LoginOtpStore = new();
    private static readonly ConcurrentDictionary<string, TwoFactorSettingsEntry> TwoFactorSettingsStore = new();

    private readonly IUserRepository _userRepository;
    private readonly IConfiguration? _configuration;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService? _emailService;
    private readonly IRegistrationOtpStore? _registrationOtpStore;

    public AuthService(
        IUserRepository userRepository,
        IConfiguration? configuration = null,
        IPasswordHasher? passwordHasher = null,
        IEmailService? emailService = null,
        IRegistrationOtpStore? registrationOtpStore = null)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _passwordHasher = passwordHasher ?? new PasswordHasher();
        _emailService = emailService;
        _registrationOtpStore = registrationOtpStore;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        AuthRequestValidation.EnsureValid(new LoginRequestValidator(), request);

        var user = await _userRepository.GetByUsernameAsync(request.UsernameOrEmail.Trim(), cancellationToken)
            ?? throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");

        await VerifyPasswordAsync(user, request.Password, cancellationToken);

        if (user.IsTwoFactorEnabled)
            throw new UnauthorizedAccessException("Tài khoản yêu cầu xác thực OTP để đăng nhập.");

        return await IssueLoginSessionAsync(user, request.TenantCode, cancellationToken);
    }

    public async Task<SendLoginOtpResponse> SendLoginOtpAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthRequestValidation.EnsureValid(new LoginRequestValidator(), request);

        var user = await _userRepository.GetByUsernameAsync(request.UsernameOrEmail.Trim(), cancellationToken)
            ?? throw new UnauthorizedAccessException("Tài khoản hoặc mật khẩu không chính xác.");

        await VerifyPasswordAsync(user, request.Password, cancellationToken);

        if (!user.IsTwoFactorEnabled)
        {
            var session = await IssueLoginSessionAsync(user, request.TenantCode, cancellationToken);
            return new SendLoginOtpResponse(false, string.Empty, string.Empty, string.Empty, 0, false,
                "Đăng nhập thành công.", User: session.User, Session: session);
        }

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

        var isDevelopment = string.Equals(_configuration?["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase);
        if (!isRealEmailSent && !isDevelopment)
        {
            LoginOtpStore.TryRemove(tempToken, out _);
            throw new InvalidOperationException("Không thể gửi mã xác thực. Vui lòng thử lại sau.");
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
            DevOtpCode: isDevelopment && !isRealEmailSent ? otpCode : null
        );
    }

    public async Task<LoginResponse> VerifyLoginOtpAsync(VerifyLoginOtpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthRequestValidation.EnsureValid(new VerifyLoginOtpRequestValidator(), request);

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

    public async Task<TwoFactorStatusResponse> GetTwoFactorStatusAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new UnauthorizedAccessException("Tài khoản không tồn tại.");
        return new TwoFactorStatusResponse(user.IsTwoFactorEnabled, MaskEmailDisplay(user.Email), user.IsEmailVerified);
    }

    public async Task<TwoFactorChallengeResponse> CreateTwoFactorChallengeAsync(string userId, TwoFactorChallengeRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new UnauthorizedAccessException("Tài khoản không tồn tại.");
        if (request.Enabled && !user.IsEmailVerified) throw new ArgumentException("Hãy xác minh email trước khi bật xác thực 2 lớp.");
        await VerifyPasswordAsync(user, request.CurrentPassword, cancellationToken);
        if (string.IsNullOrWhiteSpace(user.Email)) throw new ArgumentException("Tài khoản chưa có email để nhận mã xác thực.");

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var token = $"otp_2fa_settings_{Guid.NewGuid():N}";
        TwoFactorSettingsStore[token] = new TwoFactorSettingsEntry(user.Id, request.Enabled, otp, DateTime.UtcNow.AddMinutes(5));
        var sent = _emailService != null && await _emailService.SendTwoFactorSettingsOtpAsync(user.Email, user.FullName, otp, cancellationToken);
        var isDevelopment = string.Equals(_configuration?["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase);
        if (!sent && !isDevelopment)
        {
            TwoFactorSettingsStore.TryRemove(token, out _);
            throw new InvalidOperationException("Không thể gửi mã xác thực. Vui lòng thử lại sau.");
        }
        return new TwoFactorChallengeResponse(token, MaskEmailDisplay(user.Email), 300, isDevelopment && !sent ? otp : null);
    }

    public async Task<TwoFactorStatusResponse> ConfirmTwoFactorChallengeAsync(string userId, TwoFactorConfirmRequest request, CancellationToken cancellationToken = default)
    {
        if (!TwoFactorSettingsStore.TryRemove(request.ChallengeToken.Trim(), out var challenge) || challenge.UserId != userId)
            throw new UnauthorizedAccessException("Phiên xác thực không hợp lệ hoặc đã được sử dụng.");
        if (challenge.ExpiresAtUtc < DateTime.UtcNow || !string.Equals(challenge.OtpCode, request.OtpCode.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Mã xác thực không chính xác hoặc đã hết hạn.");
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new UnauthorizedAccessException("Tài khoản không tồn tại.");
        user.IsTwoFactorEnabled = challenge.Enabled;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);
        return new TwoFactorStatusResponse(user.IsTwoFactorEnabled, MaskEmailDisplay(user.Email), user.IsEmailVerified);
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
        ArgumentNullException.ThrowIfNull(request);
        AuthRequestValidation.EnsureValid(new RegisterTenantRequestValidator(), request);

        var userEmail = request.Email.Trim().ToLowerInvariant();
        var normalizedPhone = NormalizePhoneKey(request.Phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            throw new ArgumentException("Số điện thoại không hợp lệ.");

        var isDuplicate = await _userRepository.ExistsByPhoneOrEmailAsync(normalizedPhone, userEmail, cancellationToken);
        if (isDuplicate)
        {
            throw new ArgumentException("Số điện thoại hoặc email đã được đăng ký trong hệ thống.");
        }

        var challenge = await GetRegistrationOtpAsync(request.ChallengeId, userEmail, cancellationToken);
        if (challenge == null)
        {
            throw new ArgumentException("OTP_REQUIRED: Mã xác thực OTP không tồn tại hoặc đã hết hạn. Vui lòng bấm gửi lại mã.");
        }

        var utcNow = DateTime.UtcNow;
        if (challenge.ExpiresAtUtc <= utcNow)
        {
            await RemoveRegistrationOtpAsync(challenge.ChallengeId, cancellationToken);
            throw new ArgumentException("OTP_EXPIRED: Mã xác thực OTP đã hết hạn (quá 5 phút). Vui lòng yêu cầu gửi lại mã mới.");
        }

        if (!string.Equals(challenge.NormalizedEmail, userEmail, StringComparison.Ordinal))
        {
            throw new ArgumentException("OTP_EMAIL_MISMATCH: Email đăng ký không khớp với yêu cầu OTP ban đầu.");
        }

        var expectedIntent = request.StartTrial ? "TRIAL" : "NO_PLAN";
        if (!string.Equals(challenge.Intent, "ANY", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(challenge.Intent, expectedIntent, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("OTP_INTENT_MISMATCH: Mã OTP không khớp với luồng đăng ký hiện tại. Vui lòng yêu cầu gửi lại mã mới.");
        }

        var expectedFingerprint = ComputeRegistrationFingerprint(userEmail, normalizedPhone, request.StoreName);
        if (!string.IsNullOrWhiteSpace(challenge.RequestFingerprint) &&
            !string.Equals(challenge.RequestFingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            throw new ArgumentException("OTP_FINGERPRINT_MISMATCH: Thông tin đăng ký đã thay đổi sau khi gửi OTP. Vui lòng yêu cầu gửi lại mã mới.");
        }

        await VerifyChallengeOtpHashAsync(challenge, request.OtpCode, cancellationToken);

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"reg_{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();

        var reserved = await TryReserveRegistrationOtpAsync(challenge.ChallengeId, idempotencyKey, utcNow, cancellationToken);
        if (!reserved)
        {
            throw new InvalidOperationException("OTP_ALREADY_USED: Mã xác thực OTP đang được xử lý hoặc đã được sử dụng.");
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

        var trialStartedAt = request.StartTrial ? DateTime.UtcNow : (DateTime?)null;
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = request.StoreName.Trim(),
            Subdomain = $"{cleanSlug}-{uniqueSuffix}",
            Code = tenantCode.Length > 20 ? tenantCode.Substring(0, 20) : tenantCode,
            Hotline = normalizedPhone,
            Status = "ACTIVE",
            CurrentPlan = request.StartTrial ? "TRIAL" : null,
            SubscriptionStatus = request.StartTrial ? "TRIAL" : "NO_PLAN",
            HasStaffManagementAddon = request.StartTrial,
            WalletBalance = 0m,
            TrialStartedAt = trialStartedAt,
            TrialExpiresAt = trialStartedAt?.AddHours(72),
            SubscriptionExpiresAt = trialStartedAt?.AddHours(72)
        };

        var userId = $"usr_{Guid.NewGuid():N}";
        var refreshToken = $"mt_refresh_{Guid.NewGuid():N}";
        var user = new User
        {
            Id = userId,
            UserName = normalizedPhone,
            FullName = request.FullName.Trim(),
            Email = userEmail,
            NormalizedEmail = userEmail,
            Phone = normalizedPhone,
            NormalizedPhone = normalizedPhone,
            TrialConsumedAt = trialStartedAt,
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
            Phone = normalizedPhone,
            IsActive = true
        };

        var auditLog = new AuditLog
        {
            TenantId = tenantId,
            ActorId = userId,
            ActorName = user.FullName,
            ActorRole = UserRoleNames.StoreOwner,
            ActionType = request.StartTrial ? "TRIAL_ACTIVATED" : "TENANT_REGISTERED",
            TargetEntity = "Tenant",
            TargetId = tenantId,
            NewValuesJson = JsonSerializer.Serialize(new
            {
                idempotencyKey,
                challengeId = challenge.ChallengeId,
                startTrial = request.StartTrial,
                subscriptionStatus = tenant.SubscriptionStatus,
                trialStartedAt = tenant.TrialStartedAt,
                trialExpiresAt = tenant.TrialExpiresAt
            })
        };

        try
        {
            await _userRepository.RegisterStoreOwnerAsync(user, tenant, branch, auditLog, cancellationToken);
        }
        catch
        {
            await ReleaseRegistrationOtpReservationAsync(challenge.ChallengeId, idempotencyKey, DateTime.UtcNow, cancellationToken);
            throw;
        }

        await ConsumeRegistrationOtpAsync(challenge.ChallengeId, idempotencyKey, cancellationToken);

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
        ArgumentNullException.ThrowIfNull(request);
        AuthRequestValidation.EnsureValid(new SendRegistrationOtpRequestValidator(), request);

        var emailTrimmed = request.Email.Trim().ToLowerInvariant();
        if (!TryValidateEmail(emailTrimmed))
        {
            throw new ArgumentException("Địa chỉ email không đúng định dạng.");
        }

        EnforceSendOtpIdentifierRateLimit(emailTrimmed);

        var normalizedPhone = NormalizePhoneKey(request.Phone ?? string.Empty);

        var isDuplicate = await _userRepository.ExistsByPhoneOrEmailAsync(
            normalizedPhone,
            emailTrimmed,
            cancellationToken);

        if (isDuplicate)
        {
            throw new ArgumentException("Số điện thoại hoặc email này đã được đăng ký cửa hàng trên hệ thống.");
        }

        var challengeId = $"regotp_{Guid.NewGuid():N}";
        var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        var intent = request.StartTrial.HasValue ? (request.StartTrial.Value ? "TRIAL" : "NO_PLAN") : "ANY";
        var fingerprint = ComputeRegistrationFingerprint(emailTrimmed, normalizedPhone, request.StoreName);

        var challengeState = new RegistrationOtpState(
            ChallengeId: challengeId,
            NormalizedEmail: emailTrimmed,
            OtpHash: HashChallengeOtp(challengeId, otpCode),
            Intent: intent,
            RequestFingerprint: fingerprint,
            State: RegistrationOtpStates.Pending,
            ReservedByIdempotencyKey: null,
            ExpiresAtUtc: expiresAtUtc,
            FailedAttempts: 0);

        await CreateRegistrationOtpAsync(challengeState, cancellationToken);

        var isRealEmailSent = false;
        if (_emailService != null)
        {
            isRealEmailSent = await _emailService.SendRegistrationOtpAsync(
                emailTrimmed,
                request.FullName ?? "Chủ cửa hàng",
                request.StoreName ?? "MT-GRMS",
                otpCode,
                cancellationToken);
        }

        var isDevelopment = IsDevelopmentEnvironment();
        if (!isRealEmailSent && !isDevelopment)
        {
            await RemoveRegistrationOtpAsync(challengeId, cancellationToken);
            throw new InvalidOperationException("Không thể gửi mã xác thực. Vui lòng thử lại sau.");
        }

        var maskedEmail = MaskEmailDisplay(emailTrimmed);
        var message = isRealEmailSent
            ? $"Mã xác thực Email OTP đã được gửi tới hộp thư {maskedEmail}."
            : $"Đã tạo mã xác thực Email OTP cho {maskedEmail}.";

        return new SendRegistrationOtpResponse(
            Success: true,
            Message: message,
            MaskedEmail: maskedEmail,
            ExpiresInSeconds: 300,
            IsRealEmailSent: isRealEmailSent,
            DevOtpCode: !isRealEmailSent && isDevelopment ? otpCode : null,
            ChallengeId: challengeId
        );
    }

    public async Task<bool> VerifyRegistrationOtpAsync(
        VerifyRegistrationOtpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AuthRequestValidation.EnsureValid(new VerifyRegistrationOtpRequestValidator(), request);

        var normalizedEmail = request.EmailOrPhone.Trim().ToLowerInvariant();
        var entry = await GetRegistrationOtpAsync(null, normalizedEmail, cancellationToken);
        if (entry == null)
        {
            throw new ArgumentException("Mã xác thực OTP không tồn tại hoặc đã hết hạn. Vui lòng bấm gửi lại mã.");
        }

        if (entry.ExpiresAtUtc <= DateTime.UtcNow)
        {
            await RemoveRegistrationOtpAsync(entry.ChallengeId, cancellationToken);
            throw new ArgumentException("Mã xác thực OTP đã hết hạn (quá 5 phút). Vui lòng yêu cầu gửi lại mã mới.");
        }

        await VerifyChallengeOtpHashAsync(entry, request.OtpCode, cancellationToken);
        return true;
    }

    private async Task VerifyChallengeOtpHashAsync(RegistrationOtpState entry, string otpCode, CancellationToken cancellationToken)
    {
        var cleanOtp = otpCode.Trim();
        var actualHash = Convert.FromHexString(entry.OtpHash);
        var candidateHash = Convert.FromHexString(HashChallengeOtp(entry.ChallengeId, cleanOtp));
        if (!CryptographicOperations.FixedTimeEquals(actualHash, candidateHash))
        {
            var nextFailures = await IncrementRegistrationOtpFailureAsync(entry.ChallengeId, MaxOtpFailedAttempts, cancellationToken);
            if (nextFailures >= MaxOtpFailedAttempts)
            {
                throw new ArgumentException("OTP_ATTEMPTS_EXCEEDED: Bạn đã nhập sai mã OTP quá 5 lần. Vui lòng yêu cầu gửi lại mã mới.");
            }

            throw new ArgumentException($"OTP_INVALID: Mã xác thực Email OTP không chính xác (còn {MaxOtpFailedAttempts - nextFailures} lần thử).");
        }
    }

    private async Task CreateRegistrationOtpAsync(RegistrationOtpState state, CancellationToken cancellationToken)
    {
        if (_registrationOtpStore != null)
        {
            await _registrationOtpStore.CreateAsync(state, cancellationToken);
            return;
        }

        lock (InMemoryOtpSync)
        {
            foreach (var oldKey in RegistrationOtpStore.Where(kvp =>
                         kvp.Value.NormalizedEmail == state.NormalizedEmail &&
                         kvp.Value.State != RegistrationOtpStates.Consumed).Select(kvp => kvp.Key).ToList())
            {
                RegistrationOtpStore.TryRemove(oldKey, out _);
            }
            RegistrationOtpStore[state.ChallengeId] = state;
        }
    }

    private async Task<RegistrationOtpState?> GetRegistrationOtpAsync(string? challengeId, string normalizedEmail, CancellationToken cancellationToken)
    {
        if (_registrationOtpStore != null)
        {
            return await _registrationOtpStore.GetAsync(challengeId, normalizedEmail, cancellationToken);
        }

        lock (InMemoryOtpSync)
        {
            if (!string.IsNullOrWhiteSpace(challengeId))
            {
                return RegistrationOtpStore.TryGetValue(challengeId, out var found) ? found : null;
            }

            return RegistrationOtpStore.Values
                .Where(x => x.NormalizedEmail == normalizedEmail && x.State != RegistrationOtpStates.Consumed)
                .OrderByDescending(x => x.ExpiresAtUtc)
                .FirstOrDefault();
        }
    }

    private async Task<bool> TryReserveRegistrationOtpAsync(string challengeId, string idempotencyKey, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_registrationOtpStore != null)
        {
            return await _registrationOtpStore.TryReserveAsync(challengeId, idempotencyKey, utcNow, cancellationToken);
        }

        lock (InMemoryOtpSync)
        {
            if (!RegistrationOtpStore.TryGetValue(challengeId, out var row) || row.ExpiresAtUtc <= utcNow)
                return false;

            if (row.State == RegistrationOtpStates.Pending ||
                (row.State == RegistrationOtpStates.Reserved && row.ReservedByIdempotencyKey == idempotencyKey))
            {
                RegistrationOtpStore[challengeId] = row with
                {
                    State = RegistrationOtpStates.Reserved,
                    ReservedByIdempotencyKey = idempotencyKey
                };
                return true;
            }

            return false;
        }
    }

    private async Task ReleaseRegistrationOtpReservationAsync(string challengeId, string idempotencyKey, DateTime utcNow, CancellationToken cancellationToken)
    {
        if (_registrationOtpStore != null)
        {
            await _registrationOtpStore.ReleaseReservationAsync(challengeId, idempotencyKey, utcNow, cancellationToken);
            return;
        }

        lock (InMemoryOtpSync)
        {
            if (RegistrationOtpStore.TryGetValue(challengeId, out var row) &&
                row.State == RegistrationOtpStates.Reserved &&
                row.ReservedByIdempotencyKey == idempotencyKey &&
                row.ExpiresAtUtc > utcNow)
            {
                RegistrationOtpStore[challengeId] = row with
                {
                    State = RegistrationOtpStates.Pending,
                    ReservedByIdempotencyKey = null
                };
            }
        }
    }

    private async Task ConsumeRegistrationOtpAsync(string challengeId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (_registrationOtpStore != null)
        {
            await _registrationOtpStore.ConsumeAsync(challengeId, idempotencyKey, cancellationToken);
            return;
        }

        lock (InMemoryOtpSync)
        {
            if (RegistrationOtpStore.TryGetValue(challengeId, out var row) &&
                row.State == RegistrationOtpStates.Reserved &&
                row.ReservedByIdempotencyKey == idempotencyKey)
            {
                RegistrationOtpStore[challengeId] = row with { State = RegistrationOtpStates.Consumed };
            }
        }
    }

    private async Task<int> IncrementRegistrationOtpFailureAsync(string challengeId, int maxAttempts, CancellationToken cancellationToken)
    {
        if (_registrationOtpStore != null)
        {
            return await _registrationOtpStore.IncrementFailureOrRemoveAsync(challengeId, maxAttempts, cancellationToken);
        }

        lock (InMemoryOtpSync)
        {
            if (!RegistrationOtpStore.TryGetValue(challengeId, out var row))
                return maxAttempts;

            var next = row.FailedAttempts + 1;
            if (next >= maxAttempts)
            {
                RegistrationOtpStore.TryRemove(challengeId, out _);
            }
            else
            {
                RegistrationOtpStore[challengeId] = row with { FailedAttempts = next };
            }
            return next;
        }
    }

    private async Task RemoveRegistrationOtpAsync(string challengeId, CancellationToken cancellationToken)
    {
        if (_registrationOtpStore != null)
        {
            await _registrationOtpStore.RemoveAsync(challengeId, cancellationToken);
            return;
        }
        RegistrationOtpStore.TryRemove(challengeId, out _);
    }

    private bool IsDevelopmentEnvironment()
    {
        var configEnv = _configuration?["ASPNETCORE_ENVIRONMENT"] ?? _configuration?["Environment"];
        if (!string.IsNullOrWhiteSpace(configEnv))
        {
            return string.Equals(configEnv, "Development", StringComparison.OrdinalIgnoreCase);
        }

        var processEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return string.Equals(processEnv, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 100) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return string.Equals(addr.Address, email, StringComparison.OrdinalIgnoreCase) && email.Contains('.');
        }
        catch
        {
            return false;
        }
    }

    private static void EnforceSendOtpIdentifierRateLimit(string normalizedEmail)
    {
        var now = DateTime.UtcNow;
        var queue = RegistrationOtpSendWindows.GetOrAdd(normalizedEmail, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > SendOtpRateLimitWindow)
            {
                queue.Dequeue();
            }

            if (queue.Count >= MaxSendOtpPerWindow)
            {
                throw new InvalidOperationException("RATE_LIMIT_EXCEEDED: Bạn đã yêu cầu gửi mã OTP quá nhiều lần. Vui lòng thử lại sau ít phút.");
            }

            queue.Enqueue(now);
        }
    }

    private static string HashChallengeOtp(string challengeId, string otp)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId}:{otp}")));

    private static string ComputeRegistrationFingerprint(string normalizedEmail, string normalizedPhone, string? storeName)
    {
        var normalizedStore = (storeName ?? string.Empty).Trim().ToLowerInvariant();
        var raw = string.IsNullOrWhiteSpace(normalizedPhone)
            ? normalizedEmail
            : $"{normalizedEmail}|{normalizedPhone}|{normalizedStore}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
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
