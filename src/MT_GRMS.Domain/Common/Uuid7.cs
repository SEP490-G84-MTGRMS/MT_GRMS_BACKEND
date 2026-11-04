using System.Security.Cryptography;

namespace MT_GRMS.Domain.Common;

/// <summary>
/// Bộ sinh UUIDv7 chuẩn RFC 9562 dành cho .NET 8:
/// - 48 bits đầu là Unix Timestamp tính bằng mili-giây giúp sắp xếp tuần tự theo thời gian (Time-ordered).
/// - 74 bits ngẫu nhiên bảo đảm tính độc nhất toàn cầu và bảo mật.
/// - Triệt tiêu phân mảnh chỉ mục B+Tree (Page Splits) trong MySQL (InnoDB) và SQL Server.
/// </summary>
public static class Uuid7
{
    public static Guid NewGuid()
    {
        // 1. Lấy Unix timestamp hiện tại tính theo mili-giây (48 bits)
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 2. Sinh ngẫu nhiên entropy
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        // 3. Ghi 48-bit timestamp vào 6 bytes đầu (Big-Endian)
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        // 4. Thiết lập Version 7 (0b0111) vào 4 bits cao của byte 6
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // 5. Thiết lập Variant RFC 4122 / RFC 9562 (0b10) vào 2 bits cao của byte 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // 6. Khởi tạo Guid từ bytes theo chuẩn Big-Endian để string format sắp xếp tuần tự
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// Sinh chuỗi UUIDv7 dạng string (ví dụ: "018f6c3a-92b1-7a54-8c01-7890abcdef12")
    /// </summary>
    public static string NewString() => NewGuid().ToString();
}
