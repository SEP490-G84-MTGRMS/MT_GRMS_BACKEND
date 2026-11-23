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
[Authorize(Roles = SystemRoles.StoreOwner)]
public class FinanceAccountsController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;

    public FinanceAccountsController(AppDbContext db, IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtectionProvider, IConfiguration configuration)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _protector = dataProtectionProvider.CreateProtector("MT_GRMS.SePay.ApiToken.v1");
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<FinanceAccountDto>>>> GetAll([FromQuery] Guid? branchId, CancellationToken ct)
    {
        var query = _db.FinanceAccounts.AsNoTracking().Where(x => !x.IsDeleted);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId || x.BranchId == null);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var accounts = await query.OrderBy(x => x.AccountType).ThenBy(x => x.AccountName).ToListAsync(ct);
        return Ok(ApiResponse<List<FinanceAccountDto>>.Ok(accounts.Select(x => Map(x, branches)).ToList()));
    }

    [HttpGet("sepay/transactions")]
    public async Task<IActionResult> GetSepayTransactions(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? financeAccountId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 9,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PaymentTransactions.AsNoTracking();

        if (financeAccountId.HasValue && financeAccountId.Value != Guid.Empty)
        {
            query = query.Where(x => x.FinanceAccountId == financeAccountId.Value);
        }
        else if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(x => x.BranchId == branchId.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var rawItems = await query.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.BranchId,
                x.FinanceAccountId,
                x.BankTransactionId,
                TransactionDate = DateTime.SpecifyKind(x.TransactionDate, DateTimeKind.Utc),
                x.AmountIn,
                x.TransactionContent,
                x.ReferenceNumber,
                x.Gateway,
                x.VerifiedBySepayApi
            })
            .ToListAsync(ct);

        var orderIds = rawItems.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().ToList();
        var orderCodes = await _db.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OrderCode, ct);

        var accountIds = rawItems.Where(x => x.FinanceAccountId.HasValue).Select(x => x.FinanceAccountId!.Value).Distinct().ToList();
        var accountsList = await _db.FinanceAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.AccountName, a.AccountNumber, a.AccountHolderName, a.BankName, a.BankCode, a.BranchId })
            .ToListAsync(ct);
        var accountDict = accountsList.ToDictionary(a => a.Id, a => a);

        var branchIds = rawItems.Where(x => x.BranchId.HasValue).Select(x => x.BranchId!.Value)
            .Union(accountsList.Where(a => a.BranchId.HasValue).Select(a => a.BranchId!.Value))
            .Distinct().ToList();
        var branchNames = await _db.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var items = rawItems.Select(x =>
        {
            var foundAcc = x.FinanceAccountId.HasValue && accountDict.TryGetValue(x.FinanceAccountId.Value, out var a) ? a : null;
            string? accBranchName = null;
            if (foundAcc != null)
            {
                if (foundAcc.BranchId.HasValue && branchNames.TryGetValue(foundAcc.BranchId.Value, out var foundBranchName))
                    accBranchName = foundBranchName;
                else if (!foundAcc.BranchId.HasValue)
                    accBranchName = "Toàn hệ thống";
            }

            return new
            {
                x.Id,
                x.OrderId,
                OrderCode = x.OrderId.HasValue && orderCodes.TryGetValue(x.OrderId.Value, out var code) ? code : null,
                x.BranchId,
                BranchName = x.BranchId.HasValue && branchNames.TryGetValue(x.BranchId.Value, out var orderBranchName) ? orderBranchName : null,
                x.FinanceAccountId,
                AccountName = foundAcc?.AccountName,
                AccountNumber = foundAcc?.AccountNumber,
                AccountHolderName = foundAcc?.AccountHolderName,
                BankName = foundAcc?.BankName,
                BankCode = foundAcc?.BankCode,
                AccountBranchId = foundAcc?.BranchId,
                AccountBranchName = accBranchName,
                x.BankTransactionId,
                TransactionDate = DateTime.SpecifyKind(x.TransactionDate, DateTimeKind.Utc),
                x.AmountIn,
                x.TransactionContent,
                x.ReferenceNumber,
                x.Gateway,
                x.VerifiedBySepayApi
            };
        }).ToList();

        return Ok(ApiResponse<object>.Ok(new { items, totalCount, page, pageSize }));
    }

    [HttpGet("{id:guid}/sepay/transactions")]
    public Task<IActionResult> GetSepayTransactionsByAccount(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 9, CancellationToken ct = default)
    {
        return GetSepayTransactions(null, id, page, pageSize, ct);
    }

    [HttpGet("vietqr-default")]
    public async Task<ActionResult<ApiResponse<VietQrAccountDto>>> GetVietQrDefault([FromQuery] Guid? branchId, CancellationToken ct)
    {
        var branch = branchId.HasValue
            ? await _db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == branchId.Value && !x.IsDeleted, ct)
            : null;
        var account = branch?.CurrentBankFinanceAccountId is Guid bankId
            ? await _db.FinanceAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bankId &&
                x.TenantId == branch.TenantId && x.BranchId == branch.Id &&
                x.AccountType == FinanceAccountType.Bank && x.IsActive && !x.IsDeleted, ct)
            : null;
        if (branch == null || !await _db.Tenants.AnyAsync(x => x.Id == branch.TenantId && x.IsSepayEnabled && !x.IsDeleted, ct))
            return NotFound(ApiResponse<VietQrAccountDto>.Fail("SEPAY_NOT_ENABLED", "Cửa hàng chưa bật SePay để nhận VietQR."));
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
        await EnsureSingleDefault(account, ct);
        _db.FinanceAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
        if (account.AccountType == FinanceAccountType.Bank && request.IsDefaultForVietQr && account.BranchId.HasValue)
        {
            var branch = await _db.Branches.FirstAsync(x => x.Id == account.BranchId.Value, ct);
            branch.CurrentBankFinanceAccountId = account.Id;
            await _db.SaveChangesAsync(ct);
        }
        return Ok(ApiResponse<FinanceAccountDto>.Ok(Map(account, await BranchNames(ct)), "Đã thêm tài khoản/két quỹ."));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<FinanceAccountDto>>> Update(Guid id, [FromBody] SaveFinanceAccountRequest request, CancellationToken ct)
    {
        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (account == null) return NotFound(ApiResponse<FinanceAccountDto>.Fail("FINANCE_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản/két quỹ."));
        var validation = await ValidateRequest(request, id, ct);
        if (validation != null) return BadRequest(ApiResponse<FinanceAccountDto>.Fail("FINANCE_ACCOUNT_INVALID", validation));
        if (account.AccountType == FinanceAccountType.Bank &&
            (!string.Equals(account.BankCode, request.BankCode, StringComparison.OrdinalIgnoreCase) ||
             account.AccountNumber != request.AccountNumber || account.BranchId != request.BranchId))
            return Conflict(ApiResponse<FinanceAccountDto>.Fail("BANK_IDENTITY_IMMUTABLE", "Không sửa tài khoản Bank cũ; hãy thêm tài khoản mới để giữ lịch sử."));
        Apply(account, request);
        await EnsureSingleDefault(account, ct);
        account.UpdatedAt = DateTime.UtcNow;
        if (account.AccountType == FinanceAccountType.Bank && account.BranchId.HasValue && request.IsDefaultForVietQr)
        {
            var branch = await _db.Branches.FirstAsync(x => x.Id == account.BranchId.Value, ct);
            branch.CurrentBankFinanceAccountId = account.Id;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<FinanceAccountDto>.Ok(Map(account, await BranchNames(ct)), "Đã cập nhật tài khoản/két quỹ."));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<bool>>> SetStatus(Guid id, [FromBody] SetFinanceAccountStatusRequest request, CancellationToken ct)
    {
        var account = await _db.FinanceAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (account == null) return NotFound(ApiResponse<bool>.Fail("FINANCE_ACCOUNT_NOT_FOUND", "Không tìm thấy tài khoản/két quỹ."));
        if (!request.IsActive && account.ActiveShiftId.HasValue) return Conflict(ApiResponse<bool>.Fail("FINANCE_ACCOUNT_SHIFT_OPEN", "Không thể ngừng két đang có ca POS mở."));
        if (!request.IsActive && account.AccountType == FinanceAccountType.Bank && account.BranchId.HasValue &&
            await _db.Branches.AnyAsync(x => x.Id == account.BranchId.Value && x.CurrentBankFinanceAccountId == account.Id, ct))
            return Conflict(ApiResponse<bool>.Fail("BANK_CURRENT", "Đổi tài khoản nhận QR của chi nhánh trước khi ngừng tài khoản này."));
        if (request.IsActive && account.AccountType == FinanceAccountType.Bank && account.SepayConnectionStatus != "Connected")
            return Conflict(ApiResponse<bool>.Fail("SEPAY_REQUIRED", "Phải kết nối và kiểm tra SePay thành công trước khi kích hoạt tài khoản ngân hàng."));
        account.IsActive = request.IsActive;
        account.UpdatedAt = DateTime.UtcNow;

        // REQ: Mỗi chi nhánh duy trì 01 Két sắt và 01 Két quầy hoạt động; khi đổi sang két khác thì két cũ tự động tắt
        if (request.IsActive && (account.AccountType == FinanceAccountType.Safe || account.AccountType == FinanceAccountType.Till) && account.BranchId.HasValue)
        {
            var others = await _db.FinanceAccounts
                .Where(x => x.Id != account.Id && x.BranchId == account.BranchId && x.AccountType == account.AccountType && x.IsActive && !x.IsDeleted)
                .ToListAsync(ct);
            foreach (var other in others)
            {
                other.IsActive = false;
                other.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<bool>.Ok(true, request.IsActive ? "Đã kích hoạt và chuyển làm két hoạt động chính của chi nhánh." : "Đã ngừng hoạt động."));
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
            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == account.TenantId && !x.IsDeleted, ct);
            if (tenant == null) return NotFound(ApiResponse<SepayConnectionDto>.Fail("TENANT_NOT_FOUND", "Không tìm thấy cửa hàng."));
            tenant.SepayApiKeyEncrypted = "DP:" + _protector.Protect(request.ApiToken.Trim());
            tenant.SepayWebhookSecret = string.IsNullOrWhiteSpace(tenant.SepayWebhookSecret)
                ? Convert.ToHexString(Guid.NewGuid().ToByteArray()) : tenant.SepayWebhookSecret;
            tenant.IsSepayEnabled = true;
            account.SepayConnectionStatus = "Connected";
            account.SepayLastVerifiedAtUtc = DateTime.UtcNow;
            account.SepayLastError = null;
            account.IsDefaultForVietQr = true;
            account.IsActive = true;
            account.UpdatedAt = DateTime.UtcNow;
            if (account.BranchId.HasValue)
            {
                var branch = await _db.Branches.FirstOrDefaultAsync(x => x.Id == account.BranchId.Value, ct);
                if (branch != null)
                {
                    branch.CurrentBankFinanceAccountId = account.Id;
                }
            }
            await _db.SaveChangesAsync(ct);
            var publicBase = _configuration["SePay:PublicWebhookBaseUrl"]?.TrimEnd('/');
            string webhookUrl;
            if (!string.IsNullOrWhiteSpace(publicBase))
            {
                webhookUrl = publicBase.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
                    ? $"{publicBase}/payments/sepay-webhook"
                    : $"{publicBase}/api/payments/sepay-webhook";
            }
            else
            {
                webhookUrl = $"{Request.Scheme}://{Request.Host}/api/payments/sepay-webhook";
            }
            return Ok(ApiResponse<SepayConnectionDto>.Ok(new("Connected", account.SepayLastVerifiedAtUtc, null, webhookUrl, tenant.SepayWebhookSecret), "Kết nối SePay thành công."));
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
        var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == account.TenantId && !x.IsDeleted, ct);
        if (tenant != null) tenant.IsSepayEnabled = false;
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
        if (type == FinanceAccountType.Bank && (!request.BranchId.HasValue || string.IsNullOrWhiteSpace(request.BankCode) || string.IsNullOrWhiteSpace(request.AccountNumber) || string.IsNullOrWhiteSpace(request.AccountHolderName)))
            return "Tài khoản ngân hàng cần đủ ngân hàng, số tài khoản và chủ tài khoản.";
        if (request.BranchId.HasValue && !await _db.Branches.AnyAsync(x => x.Id == request.BranchId && !x.IsDeleted, ct)) return "Chi nhánh không tồn tại.";
        if (type == FinanceAccountType.Bank && await _db.FinanceAccounts.AnyAsync(x => x.Id != excludeId && x.AccountType == FinanceAccountType.Bank && x.BankCode == request.BankCode && x.AccountNumber == request.AccountNumber, ct))
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
        if (account.AccountType == FinanceAccountType.Bank)
        {
            if (!account.IsDefaultForVietQr) return;
            var others = await _db.FinanceAccounts.Where(x => x.Id != account.Id && x.AccountType == FinanceAccountType.Bank && x.BranchId == account.BranchId && x.IsDefaultForVietQr).ToListAsync(ct);
            foreach (var other in others) other.IsDefaultForVietQr = false;
        }
        else if ((account.AccountType == FinanceAccountType.Safe || account.AccountType == FinanceAccountType.Till) && account.IsActive && account.BranchId.HasValue)
        {
            var others = await _db.FinanceAccounts
                .Where(x => x.Id != account.Id && x.BranchId == account.BranchId && x.AccountType == account.AccountType && x.IsActive && !x.IsDeleted)
                .ToListAsync(ct);
            foreach (var other in others)
            {
                other.IsActive = false;
                other.UpdatedAt = DateTime.UtcNow;
            }
        }
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

    private static FinanceAccountDto Map(FinanceAccount x, IReadOnlyDictionary<Guid, string> branches) => new(x.Id, x.BranchId, x.BranchId.HasValue && branches.TryGetValue(x.BranchId.Value, out var name) ? name : "Toàn hệ thống", x.AccountName, x.AccountType.ToString(), x.BankCode, x.BankName, Mask(x.AccountNumber), x.AccountHolderName, x.CurrentBalance, x.IsActive, x.IsDefaultForVietQr, x.SepayConnectionStatus, x.SepayLastVerifiedAtUtc, x.SepayLastError);
    private static string? Mask(string? value) => string.IsNullOrWhiteSpace(value) ? value : value.Length <= 4 ? value : $"**** {value[^4..]}";
}

public record FinanceAccountDto(Guid Id, Guid? BranchId, string BranchName, string AccountName, string AccountType, string? BankCode, string? BankName, string? MaskedAccountNumber, string? AccountHolderName, decimal CurrentBalance, bool IsActive, bool IsDefaultForVietQr, string SepayConnectionStatus, DateTime? SepayLastVerifiedAtUtc, string? SepayLastError);
public record SaveFinanceAccountRequest(string AccountName, string AccountType, Guid? BranchId, string? BankCode, string? BankName, string? AccountNumber, string? AccountHolderName, bool IsDefaultForVietQr, bool IsActive = true);
public record SetFinanceAccountStatusRequest(bool IsActive);
public record ConnectSepayRequest(string ApiToken);
public record SepayConnectionDto(string Status, DateTime? VerifiedAtUtc, string? Error, string? WebhookUrl, string? WebhookAuthenticationKey);
public record VietQrAccountDto(Guid Id, string BankCode, string BankName, string AccountNumber, string AccountHolderName);
