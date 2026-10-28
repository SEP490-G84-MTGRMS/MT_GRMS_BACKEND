using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Tax.Security;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

/// <summary>
/// Implementation of IPinVerificationService using PBKDF2-HMAC-SHA256 (600,000 iterations),
/// CryptographicOperations.FixedTimeEquals constant-time comparison, and 15-minute temporary lockout.
/// </summary>
public class PinVerificationService : IPinVerificationService
{
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MaxFailedAttempts = 5;
    private const int LockDurationMinutes = 15;

    private readonly AppDbContext _context;

    public PinVerificationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PinVerificationResult> VerifyStoreOwnerPinAsync(string userId, string inputPin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new PinVerificationResult(false, "USER_ID_REQUIRED", "Không có thông tin định danh người dùng.");
        }

        if (string.IsNullOrWhiteSpace(inputPin))
        {
            return new PinVerificationResult(false, "PIN_REQUIRED", "Vui lòng nhập mã PIN xác thực.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId || u.Username == userId, ct);
        if (user == null)
        {
            return new PinVerificationResult(false, "USER_NOT_FOUND", "Không tìm thấy thông tin tài khoản người dùng.");
        }

        // 1. Kiểm tra tài khoản có đang bị tạm khóa không
        if (user.PinLockedUntilUtc.HasValue && user.PinLockedUntilUtc.Value > DateTime.UtcNow)
        {
            var remainingLockMinutes = Math.Ceiling((user.PinLockedUntilUtc.Value - DateTime.UtcNow).TotalMinutes);
            return new PinVerificationResult(
                false,
                "PIN_LOCKED",
                $"Mã PIN đã bị tạm khóa do nhập sai quá {MaxFailedAttempts} lần. Vui lòng thử lại sau {remainingLockMinutes} phút.",
                0,
                user.PinLockedUntilUtc
            );
        }

        // 2. Nếu user chưa có PIN: Khởi tạo lần đầu
        if (string.IsNullOrEmpty(user.SecurityPinHash) || string.IsNullOrEmpty(user.SecurityPinSalt))
        {
            await SetStoreOwnerPinAsync(user.Id, inputPin, ct);
            return new PinVerificationResult(true);
        }

        // 3. Tính PBKDF2 hash từ salt đã lưu
        var saltBytes = Convert.FromBase64String(user.SecurityPinSalt);
        var expectedHashBytes = Convert.FromBase64String(user.SecurityPinHash);

        var actualHashBytes = Rfc2898DeriveBytes.Pbkdf2(
            inputPin,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize
        );

        // 4. So sánh hằng số thời gian bằng FixedTimeEquals
        var isValid = CryptographicOperations.FixedTimeEquals(actualHashBytes, expectedHashBytes);

        if (isValid)
        {
            // Reset số lần sai và gỡ khóa khi nhập đúng
            if (user.FailedPinAttempts > 0 || user.PinLockedUntilUtc != null)
            {
                user.FailedPinAttempts = 0;
                user.PinLockedUntilUtc = null;
                await _context.SaveChangesAsync(ct);
            }

            return new PinVerificationResult(true);
        }

        // 5. Nếu sai: Tăng số lần thử sai
        user.FailedPinAttempts++;
        if (user.FailedPinAttempts >= MaxFailedAttempts)
        {
            user.PinLockedUntilUtc = DateTime.UtcNow.AddMinutes(LockDurationMinutes);
            await _context.SaveChangesAsync(ct);

            return new PinVerificationResult(
                false,
                "PIN_LOCKED",
                $"Bạn đã nhập sai mã PIN {MaxFailedAttempts} lần. Tài khoản bị tạm khóa trong {LockDurationMinutes} phút.",
                0,
                user.PinLockedUntilUtc
            );
        }

        await _context.SaveChangesAsync(ct);
        var remaining = MaxFailedAttempts - user.FailedPinAttempts;

        return new PinVerificationResult(
            false,
            "INVALID_PIN",
            $"Mã PIN không chính xác. Bạn còn {remaining} lần thử trước khi bị khóa tạm thời.",
            remaining,
            null
        );
    }

    public async Task SetStoreOwnerPinAsync(string userId, string newPin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPin) || newPin.Length < 4)
        {
            throw new ArgumentException("Mã PIN phải có tối thiểu 4 ký tự.");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId || u.Username == userId, ct);
        if (user == null)
        {
            throw new InvalidOperationException($"Không tìm thấy người dùng với Id '{userId}'.");
        }

        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            newPin,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize
        );

        user.SecurityPinSalt = Convert.ToBase64String(saltBytes);
        user.SecurityPinHash = Convert.ToBase64String(hashBytes);
        user.FailedPinAttempts = 0;
        user.PinLockedUntilUtc = null;

        await _context.SaveChangesAsync(ct);
    }
}
