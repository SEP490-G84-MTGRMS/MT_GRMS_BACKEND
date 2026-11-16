using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/finance-accounts")]
[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
public class FinanceAccountsController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _protector;

    public FinanceAccountsController(AppDbContext db, IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _protector = dataProtectionProvider.CreateProtector("MT_GRMS.SePay.ApiToken.v1");
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<FinanceAccountDto>>>> GetAll([FromQuery] Guid? branchId, CancellationToken ct)
    {
        await MigrateLegacyPaymentConfigAsync(ct);
        var query = _db.FinanceAccounts.AsNoTracking().Where(x => !x.IsDeleted);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId || x.BranchId == null);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var accounts = await query.OrderBy(x => x.AccountType).ThenBy(x => x.AccountName).ToListAsync(ct);
        return Ok(ApiResponse<List<FinanceAccountDto>>.Ok(accounts.Select(x => Map(x, branches)).ToList()));
    }

    [HttpGet("vietqr-default")]
    public async Task<ActionResult<ApiResponse<VietQrAccountDto>>> GetVietQrDefault([FromQuery] Guid? branchId, CancellationToken ct)
    {
        await MigrateLegacyPaymentConfigAsync(ct);
        var candidates = await _db.FinanceAccounts.AsNoTracking()
            .Where(x => x.AccountType == FinanceAccountType.Bank && x.IsActive && !x.IsDeleted &&
                        x.SepayConnectionStatus == "Connected" && (x.BranchId == branchId || x.BranchId == null))
            .ToListAsync(ct);
        var account = candidates
            .OrderByDescending(x => x.BranchId == branchId)
            .ThenByDescending(x => x.IsDefaultForVietQr)
            .FirstOrDefault();
        if (account == null)
            return NotFound(ApiResponse<VietQrAccountDto>.Fail("VIETQR_ACCOUNT_NOT_CONFIGURED", "Chi nhánh chưa có tài khoản ngân hàng đã kết nối SePay để nhận VietQR."));
        return Ok(ApiResponse<VietQrAccountDto>.Ok(new(account.Id, account.BankCode ?? string.Empty, account.BankName ?? string.Empty, account.AccountNumber ?? string.Empty, account.AccountHolderName ?? string.Empty)));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<FinanceAccountDto>>> Create([FromBody] SaveFinanceAccountRequest request, CancellationToken ct)
    {
        var validation = await ValidateRequest(request, null, ct);
        if (validation != null) return BadRequest(ApiResponse<FinanceAccountDto>.Fail("FINANCE_ACCOUNT_INVALID", validation));

        var account = new FinanceAccount();
        Apply(account, request);
        account.SepayWebhookSecret = account.AccountType == FinanceAccountType.Bank ? Convert.ToHexString(Guid.NewGuid().ToByteArray()) : null;
        await EnsureSingleDefault(account, ct);
        _db.FinanceAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<FinanceAccountDto>.Ok(Map(account, await BranchNames(ct)), "Đã thêm tài khoản/két quỹ."));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<FinanceAccountDto>>> Update(Guid id, [FromBody] SaveFinanceAccountRequest request, CancellationToken ct)
    {
        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (account == null) return NotFound(ApiResponse<FinanceAccountDto>.Fail("FINANCE_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản/két quỹ."));
        var validation = await ValidateRequest(request, id, ct);
        if (validation != null) return BadRequest(ApiResponse<FinanceAccountDto>.Fail("FINANCE_ACCOUNT_INVALID", validation));
        Apply(account, request);
        await EnsureSingleDefault(account, ct);
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<FinanceAccountDto>.Ok(Map(account, await BranchNames(ct)), "Đã cập nhật tài khoản/két quỹ."));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<bool>>> SetStatus(Guid id, [FromBody] SetFinanceAccountStatusRequest request, CancellationToken ct)
    {
        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (account == null) return NotFound(ApiResponse<bool>.Fail("FINANCE_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản/két quỹ."));
        if (!request.IsActive && account.ActiveShiftId.HasValue) return Conflict(ApiResponse<bool>.Fail("FINANCE_ACCOUNT_SHIFT_OPEN", "Không thể ngừng két đang có ca POS mở."));
        if (request.IsActive && account.AccountType == FinanceAccountType.Bank && account.SepayConnectionStatus != "Connected")
            return Conflict(ApiResponse<bool>.Fail("SEPAY_REQUIRED", "Phải kết nối và kiểm tra SePay thành công trước khi kích hoạt tài khoản ngân hàng."));
        account.IsActive = request.IsActive;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<bool>.Ok(true, request.IsActive ? "Đã kích hoạt." : "Đã ngừng hoạt động."));
    }

    [HttpPost("{id:guid}/sepay/connect")]
    public async Task<ActionResult<ApiResponse<SepayConnectionDto>>> ConnectSepay(Guid id, [FromBody] ConnectSepayRequest request, CancellationToken ct)
    {
        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (account == null || account.AccountType != FinanceAccountType.Bank)
            return NotFound(ApiResponse<SepayConnectionDto>.Fail("BANK_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản ngân hàng."));
        if (string.IsNullOrWhiteSpace(request.ApiToken))
            return BadRequest(ApiResponse<SepayConnectionDto>.Fail("SEPAY_TOKEN_REQUIRED", "API Token SePay là bắt buộc."));

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiToken.Trim());
            var url = "https://my.sepay.vn/userapi/bankaccounts/list?limit=100";
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"SePay trả về HTTP {(int)response.StatusCode}.");
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!ContainsAccountNumber(payload.RootElement, account.AccountNumber ?? string.Empty))
                throw new InvalidOperationException("Số tài khoản chưa được liên kết trong tài khoản SePay này.");
            account.SepayApiKeyEncrypted = _protector.Protect(request.ApiToken.Trim());
            account.SepayWebhookSecret ??= Convert.ToHexString(Guid.NewGuid().ToByteArray());
            account.SepayConnectionStatus = "Connected";
            account.SepayLastVerifiedAtUtc = DateTime.UtcNow;
            account.SepayLastError = null;
            account.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            var webhookUrl = $"{Request.Scheme}://{Request.Host}/api/payments/sepay-webhook";
            return Ok(ApiResponse<SepayConnectionDto>.Ok(new("Connected", account.SepayLastVerifiedAtUtc, null, webhookUrl, account.SepayWebhookSecret), "Kết nối SePay thành công."));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            account.SepayConnectionStatus = "Error";
            account.SepayLastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            account.IsActive = false;
            account.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return BadRequest(ApiResponse<SepayConnectionDto>.Fail("SEPAY_VERIFICATION_FAILED", $"Không thể xác minh SePay: {account.SepayLastError}"));
        }
    }

    [HttpDelete("{id:guid}/sepay")]
    public async Task<ActionResult<ApiResponse<bool>>> DisconnectSepay(Guid id, CancellationToken ct)
    {
        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (account == null || account.AccountType != FinanceAccountType.Bank) return NotFound(ApiResponse<bool>.Fail("BANK_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản ngân hàng."));
        account.SepayApiKeyEncrypted = null;
        account.SepayConnectionStatus = "NotConfigured";
        account.SepayLastVerifiedAtUtc = null;
        account.SepayLastError = null;
        account.IsDefaultForVietQr = false;
        account.IsActive = false;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<bool>.Ok(true, "Đã ngắt SePay và tắt nhận VietQR."));
    }

    private async Task<string?> ValidateRequest(SaveFinanceAccountRequest request, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AccountName)) return "Tên tài khoản/két là bắt buộc.";
        if (!Enum.TryParse<FinanceAccountType>(request.AccountType, true, out var type) || type is FinanceAccountType.Transit or FinanceAccountType.Suspense)
            return "Loại tài khoản chỉ được là Till, Safe hoặc Bank.";
        if (type != FinanceAccountType.Bank && !request.BranchId.HasValue) return "Két quầy và két sắt phải thuộc một chi nhánh.";
        if (type == FinanceAccountType.Bank && (string.IsNullOrWhiteSpace(request.BankCode) || string.IsNullOrWhiteSpace(request.AccountNumber) || string.IsNullOrWhiteSpace(request.AccountHolderName)))
            return "Tài khoản ngân hàng cần đủ ngân hàng, số tài khoản và chủ tài khoản.";
        if (request.BranchId.HasValue && !await _db.Branches.AnyAsync(x => x.Id == request.BranchId && !x.IsDeleted, ct)) return "Chi nhánh không tồn tại.";
        if (type == FinanceAccountType.Bank && await _db.FinanceAccounts.AnyAsync(x => x.Id != excludeId && x.AccountType == FinanceAccountType.Bank && x.BankCode == request.BankCode && x.AccountNumber == request.AccountNumber && !x.IsDeleted, ct))
            return "Tài khoản ngân hàng này đã tồn tại.";
        return null;
    }

    private static void Apply(FinanceAccount account, SaveFinanceAccountRequest request)
    {
        account.AccountType = Enum.Parse<FinanceAccountType>(request.AccountType, true);
        account.AccountName = request.AccountName.Trim();
        account.BranchId = request.BranchId;
        account.BankCode = request.BankCode?.Trim().ToUpperInvariant();
        account.BankName = request.BankName?.Trim();
        account.AccountNumber = request.AccountNumber?.Trim();
        account.AccountHolderName = request.AccountHolderName?.Trim().ToUpperInvariant();
        account.IsDefaultForVietQr = account.AccountType == FinanceAccountType.Bank && request.IsDefaultForVietQr;
        account.IsActive = account.AccountType == FinanceAccountType.Bank ? account.IsActive && account.SepayConnectionStatus == "Connected" : request.IsActive;
    }

    private async Task EnsureSingleDefault(FinanceAccount account, CancellationToken ct)
    {
        if (!account.IsDefaultForVietQr) return;
        var others = await _db.FinanceAccounts.Where(x => x.Id != account.Id && x.AccountType == FinanceAccountType.Bank && x.BranchId == account.BranchId && x.IsDefaultForVietQr).ToListAsync(ct);
        foreach (var other in others) other.IsDefaultForVietQr = false;
    }

    private async Task<Dictionary<Guid, string>> BranchNames(CancellationToken ct) => await _db.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private static bool ContainsAccountNumber(JsonElement element, string accountNumber)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals("account_number") || property.NameEquals("accountNumber")) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    string.Equals(property.Value.GetString(), accountNumber, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (ContainsAccountNumber(property.Value, accountNumber)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) if (ContainsAccountNumber(item, accountNumber)) return true;
        }
        return false;
    }

    private async Task MigrateLegacyPaymentConfigAsync(CancellationToken ct)
    {
        var legacy = await _db.StorePaymentConfigs.FirstOrDefaultAsync(x => !x.IsDeleted, ct);
        if (legacy == null || string.IsNullOrWhiteSpace(legacy.BankAccountNumber)) return;

        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x =>
            x.AccountType == FinanceAccountType.Bank &&
            x.AccountNumber == legacy.BankAccountNumber && !x.IsDeleted, ct);
        if (account == null)
        {
            account = new FinanceAccount
            {
                AccountType = FinanceAccountType.Bank,
                AccountName = $"{legacy.BankName} · Tài khoản nhận VietQR",
                BankCode = legacy.BankCode,
                BankName = legacy.BankName,
                AccountNumber = legacy.BankAccountNumber,
                AccountHolderName = legacy.BankAccountName,
                CurrentBalance = 0,
                IsActive = false,
                IsDefaultForVietQr = true,
                SepayConnectionStatus = "NotConfigured",
                SepayWebhookSecret = Convert.ToHexString(Guid.NewGuid().ToByteArray())
            };
            _db.FinanceAccounts.Add(account);
        }

        if (string.IsNullOrWhiteSpace(account.SepayApiKeyEncrypted) && !string.IsNullOrWhiteSpace(legacy.SepayApiKeyEncrypted))
            account.SepayApiKeyEncrypted = _protector.Protect(legacy.SepayApiKeyEncrypted);

        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
    }

    private static FinanceAccountDto Map(FinanceAccount x, IReadOnlyDictionary<Guid, string> branches) => new(x.Id, x.BranchId, x.BranchId.HasValue && branches.TryGetValue(x.BranchId.Value, out var name) ? name : "Toàn hệ thống", x.AccountName, x.AccountType.ToString(), x.BankCode, x.BankName, Mask(x.AccountNumber), x.AccountHolderName, x.CurrentBalance, x.IsActive, x.IsDefaultForVietQr, x.SepayConnectionStatus, x.SepayLastVerifiedAtUtc, x.SepayLastError);
    private static string? Mask(string? value) => string.IsNullOrWhiteSpace(value) ? value : value.Length <= 4 ? value : $"**** {value[^4..]}";
}

public record FinanceAccountDto(Guid Id, Guid? BranchId, string BranchName, string AccountName, string AccountType, string? BankCode, string? BankName, string? MaskedAccountNumber, string? AccountHolderName, decimal CurrentBalance, bool IsActive, bool IsDefaultForVietQr, string SepayConnectionStatus, DateTime? SepayLastVerifiedAtUtc, string? SepayLastError);
public record SaveFinanceAccountRequest(string AccountName, string AccountType, Guid? BranchId, string? BankCode, string? BankName, string? AccountNumber, string? AccountHolderName, bool IsDefaultForVietQr, bool IsActive = true);
public record SetFinanceAccountStatusRequest(bool IsActive);
public record ConnectSepayRequest(string ApiToken);
public record SepayConnectionDto(string Status, DateTime? VerifiedAtUtc, string? Error, string? WebhookUrl, string? WebhookAuthenticationKey);
public record VietQrAccountDto(Guid Id, string BankCode, string BankName, string AccountNumber, string AccountHolderName);
