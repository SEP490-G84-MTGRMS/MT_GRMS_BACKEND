using System.Security.Cryptography;

namespace MT_GRMS.Application.Auth;

/// <summary>
/// Giao diện dịch vụ băm và kiểm tra mật khẩu
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string hashedPassword, string providedPassword);
    bool VerifyPassword(string hashedPassword, string providedPassword, out bool rehashNeeded);
}

/// <summary>
/// Chuẩn mã hóa mật khẩu PBKDF2 với HMAC-SHA256 và Salt 128-bit (chuẩn ASP.NET Core)
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16; // 128-bit
    private const int KeySize = 32;  // 256-bit
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Băm mật khẩu bằng PBKDF2-HMAC-SHA256 với Salt ngẫu nhiên 128-bit
    /// </summary>
    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        // Định dạng payload: [Salt (16 bytes)] + [Key (32 bytes)]
        byte[] result = new byte[SaltSize + KeySize];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(key, 0, result, SaltSize, KeySize);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Kiểm tra mật khẩu có khớp với hash đã lưu hay không (chống tấn công timing attack)
    /// </summary>
    public static bool Verify(string hashedPassword, string providedPassword, out bool rehashNeeded)
    {
        rehashNeeded = false;
        if (string.IsNullOrEmpty(hashedPassword) || string.IsNullOrEmpty(providedPassword))
            return false;

        try
        {
            byte[] decoded = Convert.FromBase64String(hashedPassword);
            if (decoded.Length != SaltSize + KeySize)
            {
                // Nếu không đúng độ dài hash -> kiểm tra plain text để tương thích ngược
                return CheckLegacyPlaintext(hashedPassword, providedPassword, out rehashNeeded);
            }

            byte[] salt = new byte[SaltSize];
            byte[] storedKey = new byte[KeySize];
            Buffer.BlockCopy(decoded, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(decoded, SaltSize, storedKey, 0, KeySize);

            byte[] computedKey = Rfc2898DeriveBytes.Pbkdf2(providedPassword, salt, Iterations, Algorithm, KeySize);
            return CryptographicOperations.FixedTimeEquals(storedKey, computedKey);
        }
        catch (FormatException)
        {
            // Không phải Base64 -> kiểm tra plain text cũ để tương thích ngược
            return CheckLegacyPlaintext(hashedPassword, providedPassword, out rehashNeeded);
        }
    }

    private static bool CheckLegacyPlaintext(string storedPassword, string providedPassword, out bool rehashNeeded)
    {
        rehashNeeded = true;
        return storedPassword == providedPassword;
    }

    public string HashPassword(string password) => Hash(password);

    public bool VerifyPassword(string hashedPassword, string providedPassword) =>
        Verify(hashedPassword, providedPassword, out _);

    public bool VerifyPassword(string hashedPassword, string providedPassword, out bool rehashNeeded) =>
        Verify(hashedPassword, providedPassword, out rehashNeeded);
}

