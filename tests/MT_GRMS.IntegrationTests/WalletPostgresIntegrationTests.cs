using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.SaaS.Dtos;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using Npgsql;

namespace MT_GRMS.IntegrationTests;

public class WalletPostgresIntegrationTests
{
    private const string ConnectionVariable = "MT_GRMS_WALLET_TEST_POSTGRES";
    private const string TenantId = "wallet-test";

    [PostgresFact]
    public async Task LegacyOrdersAndConcurrentWebhook_PreserveBalanceAndOrderLookup()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConnectionVariable))
        {
            Database = $"wallet_test_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using var database = new AppDbContext(options, new TenantContext());
        await database.Database.EnsureCreatedAsync();
        try
        {
            database.Tenants.Add(new Tenant { Id = TenantId, Code = "WALLET-TEST", Subdomain = TenantId, WalletBalance = 75_000 });
            database.WalletTransactions.Add(new WalletTransaction
            {
                TenantId = TenantId, TransactionCode = "WAL-20261104-999", Type = "TOPUP",
                Amount = 75_000, BalanceAfter = 75_000, ReferenceCode = "LEGACY-REF"
            });
            await database.SaveChangesAsync();
            await database.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "WalletTopupOrders" (
                    "Id" text PRIMARY KEY, "TenantId" text NOT NULL, "OrderCode" text NOT NULL,
                    "Amount" numeric(18,2) NOT NULL, "TransferContent" text NOT NULL,
                    "BankCode" text NOT NULL DEFAULT 'MB', "BankName" text NOT NULL DEFAULT 'MB',
                    "AccountNumber" text NOT NULL DEFAULT '123', "AccountName" text NOT NULL DEFAULT 'Test',
                    "QrUrl" text NOT NULL DEFAULT 'https://example.test/qr', "Status" text NOT NULL,
                    "ExpiresAt" timestamptz NOT NULL DEFAULT (now() + interval '1 hour'),
                    "CompletedAt" timestamptz, "ReferenceCode" text, "CreatedBy" uuid,
                    "CreatedAt" timestamptz NOT NULL DEFAULT now(), "UpdatedAt" timestamptz,
                    "IsDeleted" boolean NOT NULL DEFAULT false
                );
                INSERT INTO "WalletTopupOrders"
                    ("Id", "TenantId", "OrderCode", "Amount", "TransferContent", "Status", "ReferenceCode")
                VALUES
                    ('legacy-paid', 'wallet-test', '000001', 75000, 'WAL 000001', 'COMPLETED', 'UNMATCHED'),
                    ('legacy-pending', 'wallet-test', '000002', 50000, 'WAL 000002', 'PENDING', NULL),
                    ('legacy-expired', 'wallet-test', '000003', 50000, 'WAL 000003', 'EXPIRED', NULL),
                    ('legacy-cancelled', 'wallet-test', '000004', 50000, 'WAL 000004', 'CANCELLED', NULL);
                """);

            await Assert.ThrowsAsync<WalletTopupSchemaMigrationException>(
                () => DbInitializer.InitializeAsync(database, seedSampleData: false));
            Assert.Equal(4L, await database.Database.SqlQueryRaw<long>(
                """SELECT count(*) AS "Value" FROM "WalletTopupOrders" """).SingleAsync());
            Assert.Single(await database.WalletTransactions.AsNoTracking().ToListAsync());

            await database.Database.ExecuteSqlRawAsync("""
                UPDATE "WalletTopupOrders" SET "ReferenceCode" = 'LEGACY-REF' WHERE "Id" = 'legacy-paid';
                """);
            await DbInitializer.InitializeAsync(database, seedSampleData: false);
            database.ChangeTracker.Clear();

            var wallet = new WalletService(database, Options.Create(new SePaySettings()));
            Assert.Equal("COMPLETED", (await wallet.GetTopupOrderStatusAsync(TenantId, "000001"))!.Status);
            Assert.Equal("PENDING", (await wallet.GetTopupOrderStatusAsync(TenantId, "000002"))!.Status);
            Assert.Equal("EXPIRED", (await wallet.GetTopupOrderStatusAsync(TenantId, "000003"))!.Status);
            Assert.Equal("CANCELLED", (await wallet.GetTopupOrderStatusAsync(TenantId, "000004"))!.Status);
            Assert.Equal(75_000, (await wallet.GetWalletBalanceAsync(TenantId)).WalletBalance);
            Assert.Single(await wallet.GetWalletTransactionsAsync(TenantId));
            Assert.True(await database.Database.SqlQueryRaw<bool>(
                """SELECT to_regclass('"WalletTopupOrders"') IS NULL AS "Value" """).SingleAsync());

            var transfer = new SepayWebhookPayload
            {
                Id = 1000, ReferenceCode = "NEW-REF", Content = "WAL 000002",
                TransferType = "in", TransferAmount = 50_000
            };
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            {
                await using var writer = new AppDbContext(options, new TenantContext());
                return await new WalletService(writer, Options.Create(new SePaySettings()))
                    .ProcessSepayWebhookAsync(transfer);
            }));
            Assert.All(results, result => Assert.True(result.Success, result.Message));

            database.ChangeTracker.Clear();
            Assert.Equal("COMPLETED", (await wallet.GetTopupOrderStatusAsync(TenantId, "000002"))!.Status);
            Assert.Equal(125_000, (await wallet.GetWalletBalanceAsync(TenantId)).WalletBalance);
            Assert.Equal(2, (await wallet.GetWalletTransactionsAsync(TenantId)).Count);
            Assert.Equal(1, await database.WalletTransactions.CountAsync(t => t.ReferenceCode == "NEW-REF"));
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            {
                Skip = $"Set {ConnectionVariable} to a test PostgreSQL server with CREATE DATABASE access.";
            }
        }
    }
}
