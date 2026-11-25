using System.IdentityModel.Tokens.Jwt;
using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Api.Authorization;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Api.Hubs;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Tax;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using MT_GRMS.Infrastructure.Workers;
using Npgsql;
using Xunit.Abstractions;

namespace MT_GRMS.IntegrationTests;

public sealed class NotificationPostgresFactAttribute : FactAttribute
{
    public const string ConnectionKey = "MT_GRMS_NOTIFICATION_TEST_POSTGRES";

    public NotificationPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionKey)))
            Skip = $"Requires an explicit {ConnectionKey} connection.";
    }
}

public class NotificationPostgresHttpTests(ITestOutputHelper output)
{
    private const string TenantId = "notification-http-test";
    private const string DueDateMigration = "20261122000000_AddExpenseVoucherDueDate";
    private const string FilingMigration = "20261122010000_AddTaxFilingConfirmations";
    private const string BranchAndNotificationMigration = "20261122020000_AddBranchConversionRulesAndNotifications";

    [NotificationPostgresFact]
    public async Task MigrationsAndTransferNotifications_WorkThroughPostgresRestAndSignalR()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var schema = $"notif_http_test_{Guid.NewGuid():N}";
        var target = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(NotificationPostgresFactAttribute.ConnectionKey))
        {
            Pooling = false, IncludeErrorDetail = false, Timeout = 15
        };
        target.Host = target.Host!.Replace("-pooler.", ".", StringComparison.OrdinalIgnoreCase);
        var isolated = new NpgsqlConnectionStringBuilder(target.ConnectionString)
        {
            SearchPath = schema, Pooling = true, MaxPoolSize = 8, ApplicationName = schema
        }.ConnectionString;
        await using var admin = new NpgsqlConnection(target.ConnectionString);
        await admin.OpenAsync(ct);
        await ExecuteAsync(admin, $"CREATE SCHEMA \"{schema}\"", ct);

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(40));
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = key });
            builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly);
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(isolated).AddInterceptors(new IsolatedSchema(schema)));
            builder.Services.AddScoped<ITenantContext, TenantContext>();
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<IUserRepository, UserRepository>();
            builder.Services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
            builder.Services.AddScoped<ITransferRepository, TransferRepository>();
            builder.Services.AddScoped<ITransferService, TransferService>();
            builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
            builder.Services.AddScoped<INotificationService, NotificationService>();
            builder.Services.AddScoped<INotificationRealtimeDispatcher, SignalRNotificationDispatcher>();
            builder.Services.AddScoped<NotificationOutbox>();
            builder.Services.AddHostedService<BusinessNotificationWorker>();
            builder.Services.AddHostedService<ExpenseReminderWorker>();
            builder.Services.AddSignalR();
            builder.Services.AddSessionAuthentication(builder.Configuration);
            builder.Services.AddAuthorization();

            await using var app = builder.Build();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseAuthentication();
            app.UseMiddleware<MultiTenantMiddleware>();
            app.UseMiddleware<SubscriptionAccessMiddleware>();
            app.UseAuthorization();
            app.MapControllers();
            app.MapHub<NotificationHub>("/hubs/notifications");

            (Guid Source, Guid Destination, Guid Product, Guid Snapshot) seed;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.OpenConnectionAsync(ct);
                Assert.Equal(schema, await db.Database.SqlQueryRaw<string>("SELECT current_schema() AS \"Value\"").SingleAsync(ct));
                await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript(), ct);
                var migrator = db.GetService<IMigrator>();
                // Start from the baseline without the columns and tables missing on existing deployments.
                await db.Database.ExecuteSqlRawAsync("""
                    DROP TABLE "TaxFilingConfirmations";
                    DROP TABLE "Notifications";
                    ALTER TABLE "ExpenseVouchers" DROP COLUMN "DueDate";
                    ALTER TABLE "Branches" DROP COLUMN "ConversionRulesJson";
                    """, ct);
                var script = migrator.GenerateScript("20261118190000_DropLegacyPaymentAccountTables", BranchAndNotificationMigration, MigrationsSqlGenerationOptions.NoTransactions);
                Assert.Contains(DueDateMigration, script);
                Assert.Contains(FilingMigration, script);
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
                    """, ct);
                await db.Database.ExecuteSqlRawAsync(script, ct);
                Assert.Contains(DueDateMigration, await db.Database.GetAppliedMigrationsAsync(ct));
                Assert.Contains(FilingMigration, await db.Database.GetAppliedMigrationsAsync(ct));
                Assert.Contains(BranchAndNotificationMigration, await db.Database.GetAppliedMigrationsAsync(ct));
                output.WriteLine("Applied all three migrations in isolated schema.");
                seed = await SeedAsync(db, ct);
            }

            await app.StartAsync(ct);
            var baseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            using var owner = Client(baseAddress, key, "owner", "STORE_OWNER");
            using var source = Client(baseAddress, key, "source", "STAFF", seed.Source);
            using var destination = Client(baseAddress, key, "destination", "STAFF", seed.Destination);
            using var sourceSocket = await ConnectAsync(baseAddress, source.DefaultRequestHeaders.Authorization!.Parameter!, ct);
            using var destinationSocket = await ConnectAsync(baseAddress, destination.DefaultRequestHeaders.Authorization!.Parameter!, ct);
            output.WriteLine("Authenticated the two branch SignalR connections.");

            var filingUrl = $"/api/tax/household/snapshots/{seed.Snapshot}/filing-confirmation";
            using var unconfirmed = await owner.GetAsync(filingUrl, ct);
            Assert.Equal(JsonValueKind.Null, (await ReadDataAsync(unconfirmed, ct)).GetProperty("confirmedAtUtc").ValueKind);
            using var confirmation = await owner.PutAsync(filingUrl, null, ct);
            var confirmedAt = (await ReadDataAsync(confirmation, ct)).GetProperty("confirmedAtUtc").GetDateTime();
            using var retryConfirmation = await owner.PutAsync(filingUrl, null, ct);
            Assert.Equal(confirmedAt, (await ReadDataAsync(retryConfirmation, ct)).GetProperty("confirmedAtUtc").GetDateTime());
            output.WriteLine("Confirmed tax filing through the API and verified idempotency.");

            using (var expenseTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                expenseTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                await WaitForExpenseReminderAsync(owner, expenseTimeout.Token);
            }
            output.WriteLine("Received the FE07 expense reminder through the REST inbox.");

            var create = new CreateTransferRequest(seed.Source, seed.Destination, false, null, "Isolated PostgreSQL flow",
                new[] { new TransferLineRequest(seed.Product, 2m, "unit", "B01", null, 500m) });
            using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
            {
                Content = JsonContent.Create(create)
            };
            createReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var created = await owner.SendAsync(createReq, ct);
            var transfer = await ReadDataAsync(created, ct);
            var id = transfer.GetProperty("id").GetGuid();

            using var submitReq = new HttpRequestMessage(HttpMethod.Post, $"/api/transfers/{id}/submit");
            submitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var submitted = await owner.SendAsync(submitReq, ct);
            submitted.EnsureSuccessStatusCode();
            await AssertPushAsync(sourceSocket, BusinessNotificationTypes.TransferDispatchRequired, ct);
            output.WriteLine("Source branch received the submit notification.");
            using var emptyInbox = await destination.GetAsync("/api/notifications", ct);
            Assert.Equal(0, (await ReadDataAsync(emptyInbox, ct)).GetProperty("unreadCount").GetInt32());

            using var dispatchReq = new HttpRequestMessage(HttpMethod.Post, $"/api/transfers/{id}/dispatch");
            dispatchReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var dispatched = await source.SendAsync(dispatchReq, ct);
            var dispatch = await ReadDataAsync(dispatched, ct);
            var pushed = await AssertPushAsync(destinationSocket, BusinessNotificationTypes.TransferReceiveRequired, ct);
            output.WriteLine("Destination branch received the dispatch notification.");
            Assert.Equal($"/employee/transfers/{id}", pushed.GetProperty("actionUrl").GetString());
            using var inbox = await destination.GetAsync("/api/notifications", ct);
            var notices = await ReadDataAsync(inbox, ct);
            Assert.Equal(1, notices.GetProperty("unreadCount").GetInt32());
            var noticeId = Assert.Single(notices.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
            using var read = await destination.PatchAsync($"/api/notifications/{noticeId}/read", null, ct);
            read.EnsureSuccessStatusCode();
            using var detail = await destination.GetAsync($"/api/transfers/{id}", ct);
            detail.EnsureSuccessStatusCode();

            var lineId = dispatch.GetProperty("items")[0].GetProperty("id").GetGuid();
            await using var returnScope = app.Services.CreateAsyncScope();
            returnScope.ServiceProvider.GetRequiredService<ITenantContext>().SetContext(TenantId, Guid.Empty, seed.Source);
            var returnDb = returnScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staleTransfer = await returnDb.Transfers.SingleAsync(transfer => transfer.Id == id, ct);

            using var receiveReq = new HttpRequestMessage(HttpMethod.Put, $"/api/transfers/{id}/receive")
            {
                Content = JsonContent.Create(new ReceiveTransferRequest("Short shipment", new[] { new ReceiveTransferLineRequest(lineId, 1, 0, null) }))
            };
            receiveReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var received = await destination.SendAsync(receiveReq, ct);
            received.EnsureSuccessStatusCode();
            await AssertPushAsync(sourceSocket, BusinessNotificationTypes.TransferReceiptDiscrepancy, ct);
            using var finalInbox = await destination.GetAsync("/api/notifications", ct);
            Assert.Equal(0, (await ReadDataAsync(finalInbox, ct)).GetProperty("unreadCount").GetInt32());

            await using var staleReturn = await returnDb.Database.BeginTransactionAsync(ct);
            var sourceStock = await returnDb.BranchStocks.SingleAsync(stock => stock.BranchId == seed.Source, ct);
            sourceStock.QuantityOnHand += 2m;
            staleTransfer.Status = TransferStatus.Completed;
            staleTransfer.CompletionResult = TransferCompletionResult.Returned;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => returnDb.SaveChangesAsync(ct));
            await staleReturn.RollbackAsync(ct);
            await returnDb.Entry(sourceStock).ReloadAsync(ct);
            await returnDb.Entry(staleTransfer).ReloadAsync(ct);
            Assert.Equal(3m, sourceStock.QuantityOnHand);
            Assert.Equal(TransferCompletionResult.ReceivedWithDiscrepancy, staleTransfer.CompletionResult);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            Assert.Matches("^notif_http_test_[0-9a-f]{32}$", schema);
            await using var pool = new NpgsqlConnection(isolated);
            NpgsqlConnection.ClearPool(pool);
            await ExecuteAsync(admin, $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", cleanup.Token);
        }
    }

    private static async Task<(Guid, Guid, Guid, Guid)> SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var source = new Branch { TenantId = TenantId, BranchCode = "A", Name = "Source" };
        var destination = new Branch { TenantId = TenantId, BranchCode = "B", Name = "Destination" };
        var category = new Category { TenantId = TenantId, CategoryCode = "C01", Name = "Test category" };
        var product = new Product { TenantId = TenantId, ProductCode = "P01", Name = "Test product", BaseUnit = "unit", CategoryId = category.Id };
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HouseholdTaxService.GetVietnamTimeZone()));
        var snapshot = new HouseholdTaxSnapshot { TenantId = TenantId, Year = today.Year, PeriodType = TaxPeriodType.Month,
            PeriodNumber = today.Month, FinalizedBy = "owner" };
        db.Tenants.Add(new Tenant { Id = TenantId, Name = "Notification test", Subdomain = "test", Code = "TEST",
            Status = "ACTIVE", SubscriptionStatus = "ACTIVE", SubscriptionExpiresAt = DateTime.UtcNow.AddDays(1) });
        db.Branches.AddRange(source, destination);
        db.Categories.Add(category);
        db.Products.Add(product);
        db.HouseholdTaxSnapshots.Add(snapshot);
        db.ExpenseVouchers.Add(new ExpenseVoucher { TenantId = TenantId, VoucherCode = "EXP-TEST-DUE", BranchId = source.Id,
            BeneficiaryName = "Synthetic vendor", Status = ExpenseVoucherStatus.Approved, DueDate = today });
        db.BranchStocks.Add(new BranchStock { TenantId = TenantId, BranchId = source.Id, ProductId = product.Id,
            BatchCode = "B01", QuantityOnHand = 5m, LandedCostPerUnit = 500m });
        foreach (var (id, role, branch) in new (string, string, Branch?)[]
                 { ("owner", "STORE_OWNER", null), ("source", "STAFF", source), ("destination", "STAFF", destination) })
        {
            var email = $"{id}@test.invalid";
            db.Users.Add(new User { Id = id, UserName = id, Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = "test-stamp" });
            db.TenantUsers.Add(new TenantUser { TenantId = TenantId, UserId = id, Role = role });
            if (branch != null)
                db.Employees.Add(new Employee { TenantId = TenantId, UserId = id, BranchId = branch.Id, EmployeeCode = id, CitizenId = id, FullName = id });
        }
        await db.SaveChangesAsync(ct);
        return (source.Id, destination.Id, product.Id, snapshot.Id);
    }

    private static async Task WaitForExpenseReminderAsync(HttpClient owner, CancellationToken ct)
    {
        while (true)
        {
            using var inbox = await owner.GetAsync("/api/notifications", ct);
            var notices = await ReadDataAsync(inbox, ct);
            if (notices.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("type").GetString() == BusinessNotificationTypes.ExpensePaymentDue))
                return;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private static HttpClient Client(Uri address, string key, string userId, string role, Guid? branch = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Role, role),
            new("tenant_id", TenantId), new("security_stamp", "test-stamp") };
        if (branch.HasValue) claims.Add(new Claim("branch_id", branch.Value.ToString()));
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("MTGRMS_API", "MTGRMS_CLIENT", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
        var client = new HttpClient { BaseAddress = address };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ClientWebSocket> ConnectAsync(Uri address, string token, CancellationToken ct)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(25));
        ct = handshakeTimeout.Token;
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("Cookie", $"access_token={token}");
            var uri = new UriBuilder(address) { Scheme = "ws", Path = "/hubs/notifications" }.Uri;
            await socket.ConnectAsync(uri, ct);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e").AsMemory(), WebSocketMessageType.Text, true, ct);
            Assert.Equal("{}", (await ReadMessageAsync(socket, ct)).TrimEnd('\u001e'));
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    private static async Task<JsonElement> AssertPushAsync(ClientWebSocket socket, string type, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        while (true)
        {
            var data = await ReadMessageAsync(socket, timeout.Token);
            foreach (var frame in data.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(frame);
                if (!json.RootElement.TryGetProperty("target", out var target) || target.GetString() != "ReceiveNotification") continue;
                var item = json.RootElement.GetProperty("arguments")[0].Clone();
                Assert.Equal(type, item.GetProperty("type").GetString());
                return item;
            }
        }
    }

    private static async Task<string> ReadMessageAsync(ClientWebSocket socket, CancellationToken ct)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        ValueWebSocketReceiveResult read;
        do
        {
            read = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            Assert.Equal(WebSocketMessageType.Text, read.MessageType);
            message.Write(buffer, 0, read.Count);
        } while (!read.EndOfMessage);
        return Encoding.UTF8.GetString(message.ToArray());
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data").Clone();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed class IsolatedSchema(string schema) : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SET search_path TO \"{schema}\"";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT current_schema()";
            Assert.Equal(schema, command.ExecuteScalar());
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SET search_path TO \"{schema}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "SELECT current_schema()";
            Assert.Equal(schema, await command.ExecuteScalarAsync(cancellationToken));
        }
    }
}
