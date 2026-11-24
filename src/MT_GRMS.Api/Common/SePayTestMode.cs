using System.Net.Http.Headers;
using System.Text.Json;

namespace MT_GRMS.Api.Common;

internal static class SePayTestMode
{
    private const string BaseUrl = "https://userapi-sandbox.sepay.vn/v2";

    internal static bool IsEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration["SePay:UseTestMode"], out var enabled) && enabled;

    internal static string BankAccountsUrl(string? accountNumber = null)
    {
        var url = $"{BaseUrl}/bank-accounts?active=1&per_page=100";
        return string.IsNullOrWhiteSpace(accountNumber) ? url : $"{url}&q={Uri.EscapeDataString(accountNumber)}";
    }

    internal static bool ContainsAccount(JsonElement response, string accountNumber) =>
        !string.IsNullOrWhiteSpace(accountNumber) &&
        ReadResults(response).Any(account => ReadString(account, "account_number") == accountNumber);

    internal static async Task<Guid?> VerifyIncomingPaymentAsync(
        HttpClient client, string token, string reference, string accountNumber, decimal amount,
        Func<string, bool> matchesOrderCode, CancellationToken ct)
    {
        if (amount <= 0) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/transactions?reference_number={Uri.EscapeDataString(reference)}&transfer_type=in&per_page=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("SePay Test Mode từ chối truy vấn đối soát.", null, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var matches = ReadResults(document.RootElement)
            .Where(tx => ReadString(tx, "reference_number") == reference &&
                         ReadString(tx, "account_number") == accountNumber &&
                         ReadString(tx, "transfer_type") == "in" &&
                         tx.TryGetProperty("amount_in", out var value) && value.ValueKind == JsonValueKind.Number &&
                         value.TryGetDecimal(out var received) && received == amount &&
                         (matchesOrderCode(ReadString(tx, "code")) ||
                          matchesOrderCode(ReadString(tx, "transaction_content"))))
            .Take(2)
            .ToArray();
        return matches.Length == 1 && Guid.TryParse(ReadString(matches[0], "id"), out var id) && id != Guid.Empty
            ? id
            : null;
    }

    internal static IEnumerable<JsonElement> ReadResults(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object || ReadString(response, "status") != "success" ||
            !response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new JsonException("Dữ liệu SePay Test Mode không đúng định dạng.");
        if (response.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
            meta.TryGetProperty("pagination", out var pagination) && pagination.ValueKind == JsonValueKind.Object &&
            pagination.TryGetProperty("has_more", out var hasMore) && hasMore.ValueKind == JsonValueKind.True)
            throw new JsonException("Có nhiều kết quả đối soát SePay Test Mode.");

        return data.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object);
    }

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
