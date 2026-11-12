using System.Security.Cryptography;
using System.Text;

namespace MT_GRMS.Application.Common;

public class TenantContext : ITenantContext
{
    public string CurrentTenantId { get; private set; } = string.Empty;
    public Guid CurrentUserId { get; private set; } = Guid.Empty;
    public string CurrentRawUserId { get; private set; } = string.Empty;
    public Guid? CurrentBranchId { get; private set; }
    public string CurrentRole { get; private set; } = string.Empty;

    public void SetContext(string tenantId, Guid userId, Guid? branchId)
    {
        CurrentTenantId = tenantId;
        CurrentUserId = userId;
        if (userId != Guid.Empty && string.IsNullOrWhiteSpace(CurrentRawUserId))
        {
            CurrentRawUserId = userId.ToString();
        }
        CurrentBranchId = branchId;
    }

    public void SetRawUserId(string? rawUserId)
    {
        CurrentRawUserId = rawUserId?.Trim() ?? string.Empty;
    }

    public void SetRole(string? role)
    {
        CurrentRole = role?.Trim() ?? string.Empty;
    }

    public static Guid ResolveDeterministicGuid(string? rawUserId)
    {
        if (string.IsNullOrWhiteSpace(rawUserId))
        {
            return Guid.Empty;
        }

        if (Guid.TryParse(rawUserId.Trim(), out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("mt-grms-user:" + rawUserId.Trim()));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        // Set version 5 / RFC 4122 variant bits to ensure non-empty valid UUID
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
