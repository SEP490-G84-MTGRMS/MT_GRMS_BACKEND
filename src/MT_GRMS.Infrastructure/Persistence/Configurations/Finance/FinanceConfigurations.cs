using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class CashbookEntryConfiguration : IEntityTypeConfiguration<CashbookEntry>
{
    public void Configure(EntityTypeBuilder<CashbookEntry> builder)
    {
        builder.ToTable("CashbookEntries");

        builder.Property(e => e.EntryCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.BalanceAfter)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.SourceType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ReferenceCode)
            .HasMaxLength(100);

        builder.Property(e => e.AuditHash)
            .HasMaxLength(256);

        builder.HasIndex(e => new { e.TenantId, e.AccountId, e.OccurredAtUtc });
        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId });
    }
}

public class FinanceAccountConfiguration : IEntityTypeConfiguration<FinanceAccount>
{
    public void Configure(EntityTypeBuilder<FinanceAccount> builder)
    {
        builder.ToTable("FinanceAccounts");

        builder.Property(a => a.AccountName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(a => a.CurrentBalance)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(a => a.BankName)
            .HasMaxLength(100);

        builder.Property(a => a.AccountNumber)
            .HasMaxLength(50);

        builder.Property(a => a.AccountHolderName)
            .HasMaxLength(200);

        builder.Property(a => a.RowVersion)
            .IsRowVersion();

        builder.HasIndex(a => new { a.TenantId, a.BranchId });
        builder.HasIndex(a => new { a.TenantId, a.AccountType });
    }
}

public class CashInTransitConfiguration : IEntityTypeConfiguration<CashInTransit>
{
    public void Configure(EntityTypeBuilder<CashInTransit> builder)
    {
        builder.ToTable("CashInTransits");

        builder.Property(c => c.TransitCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.AmountSent)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(c => c.AmountReceived)
            .HasColumnType("decimal(18,2)");

        builder.Property(c => c.DiscrepancyAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0);

        builder.HasIndex(c => new { c.TenantId, c.Status });
    }
}

public class ShiftZReportConfiguration : IEntityTypeConfiguration<ShiftZReport>
{
    public void Configure(EntityTypeBuilder<ShiftZReport> builder)
    {
        builder.ToTable("ShiftZReports");

        builder.Property(s => s.OpeningFloat).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TotalCashIn).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TotalCashOut).HasColumnType("decimal(18,2)");
        builder.Property(s => s.ExpectedCash).HasColumnType("decimal(18,2)");
        builder.Property(s => s.ActualCashCounted).HasColumnType("decimal(18,2)");
        builder.Property(s => s.Discrepancy).HasColumnType("decimal(18,2)");
        builder.Property(s => s.RetainedFloatAmount).HasColumnType("decimal(18,2)");
        builder.Property(s => s.DroppedToSafeAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(s => new { s.TenantId, s.TillId, s.Status });
    }
}

public class ExpenseVoucherConfiguration : IEntityTypeConfiguration<ExpenseVoucher>
{
    public void Configure(EntityTypeBuilder<ExpenseVoucher> builder)
    {
        builder.ToTable("ExpenseVouchers");

        builder.Property(e => e.VoucherCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.BeneficiaryName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Category)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.RefundedAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0);

        builder.Property(e => e.IsSettled)
            .HasDefaultValue(false);

        builder.Property(e => e.ReferenceCode)
            .HasMaxLength(100);

        builder.Property(e => e.InvoiceNumber)
            .HasMaxLength(100);

        builder.Property(e => e.SellerTaxCode)
            .HasMaxLength(50);

        builder.HasIndex(e => new { e.TenantId, e.BranchId, e.ExpenseDate });
        builder.HasIndex(e => new { e.TenantId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.ReferenceCode });
        builder.HasIndex(e => new { e.TenantId, e.Nature });
    }
}

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.Property(i => i.Key)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(i => i.Endpoint)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(i => i.PayloadHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(i => i.Status)
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue("PENDING");

        builder.HasIndex(i => new { i.TenantId, i.Endpoint, i.Key }).IsUnique();
    }
}
