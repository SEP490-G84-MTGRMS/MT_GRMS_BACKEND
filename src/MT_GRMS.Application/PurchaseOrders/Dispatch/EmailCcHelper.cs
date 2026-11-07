using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MT_GRMS.Application.PurchaseOrders.Dispatch;

public static class EmailCcHelper
{
    public const int DefaultMaxCc = 10;

    private static readonly Regex SimpleEmail = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> NormalizeCc(
        IEnumerable<string>? rawCc,
        string? emailTo,
        int maxCc = DefaultMaxCc)
    {
        var toNorm = (emailTo ?? string.Empty).Trim().ToLowerInvariant();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        if (rawCc == null) return result;

        foreach (var raw in rawCc)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var email = raw.Trim();
            if (!IsValidEmail(email))
            {
                throw new ArgumentException($"Email CC không hợp lệ: {email}");
            }

            if (!string.IsNullOrEmpty(toNorm) && string.Equals(email, toNorm, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Email CC không được trùng với email người nhận chính (To).");
            }

            if (!seen.Add(email.ToLowerInvariant())) continue;

            if (result.Count >= maxCc)
            {
                throw new ArgumentException($"Tối đa {maxCc} địa chỉ CC.");
            }

            result.Add(email);
        }

        return result;
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200) return false;
        if (email.Contains('\r') || email.Contains('\n')) return false;
        if (!SimpleEmail.IsMatch(email)) return false;
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ToJson(IEnumerable<string> emails) =>
        JsonSerializer.Serialize(emails.ToList());

    public static IReadOnlyList<string> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
