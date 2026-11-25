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

        builder.Property(e => e.CategoryCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.TaxTreatmentReason)
            .HasMaxLength(255);

        builder.Property(e => e.ReferenceCode)
            .HasMaxLength(100);

        builder.Property(e => e.AuditHash)
            .HasMaxLength(256);

        builder.HasIndex(e => new { e.TenantId, e.AccountId, e.OccurredAtUtc });
        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId });
        builder.HasIndex(e => new { e.TenantId, e.TaxTreatment, e.OccurredAtUtc });
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

        builder.Property(a => a.BankCode).HasMaxLength(30);
        builder.Property(a => a.Currency).HasMaxLength(3).HasDefaultValue("VND");
        builder.Property(a => a.SepayApiKeyEncrypted).HasMaxLength(4000);
        builder.Property(a => a.SepayWebhookSecret).HasMaxLength(200);
        builder.Property(a => a.SepayConnectionStatus).HasMaxLength(30).HasDefaultValue("NotConfigured");
        builder.Property(a => a.SepayLastError).HasMaxLength(500);

        builder.Property(a => a.RowVersion)
            .IsConcurrencyToken();

        builder.HasIndex(a => new { a.TenantId, a.BranchId });
        builder.HasIndex(a => new { a.TenantId, a.AccountType });
        builder.HasIndex(a => new { a.TenantId, a.BankCode, a.AccountNumber });
        builder.HasIndex(a => new { a.TenantId, a.BranchId, a.AccountType })
            .HasDatabaseName("IX_FinanceAccounts_OneActiveCashFundPerBranch")
            .IsUnique()
            .HasFilter("\"BranchId\" IS NOT NULL AND \"IsActive\" = TRUE AND NOT \"IsDeleted\" AND \"AccountType\" IN (1, 2)");
    }
}

public class ShiftZReportConfiguration : IEntityTypeConfiguration<ShiftZReport>
{
    public void Configure(EntityTypeBuilder<ShiftZReport> builder)
    {
        builder.ToTable("ShiftZReports");

        builder.Property(s => s.ShiftCode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.ShiftName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.ReceiverUserId)
            .HasMaxLength(100);

        builder.Property(s => s.Notes)
            .HasMaxLength(500);

        builder.Property(s => s.OpeningFloatExpected).HasColumnType("decimal(18,2)");
        builder.Property(s => s.OpeningFloat).HasColumnType("decimal(18,2)");
        builder.Property(s => s.OpeningDiscrepancy).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TotalCashIn).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TotalCashOut).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TotalBankRevenue).HasColumnType("decimal(18,2)");
        builder.Property(s => s.TotalCashSales).HasColumnType("decimal(18,2)");
        builder.Property(s => s.ExpectedCash).HasColumnType("decimal(18,2)");
        builder.Property(s => s.ActualCashCounted).HasColumnType("decimal(18,2)");
        builder.Property(s => s.Discrepancy).HasColumnType("decimal(18,2)");
        builder.Property(s => s.DiscrepancyReason).HasMaxLength(500);
        builder.Property(s => s.ParticipantsJson).HasColumnType("jsonb").HasDefaultValue("[]");
        builder.Property(s => s.ClosingVersion).IsConcurrencyToken().HasDefaultValue(1L);
        builder.Property(s => s.RetainedFloatAmount).HasColumnType("decimal(18,2)");
        builder.Property(s => s.DroppedToSafeAmount).HasColumnType("decimal(18,2)");

        builder.HasOne(s => s.PrevShift)
            .WithMany()
            .HasForeignKey(s => s.PrevShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Branch)
            .WithMany()
            .HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.DestinationFund)
            .WithMany()
            .HasForeignKey(s => s.DestinationFundId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.CashMovements)
            .WithOne(m => m.Shift)
            .HasForeignKey(m => m.ShiftId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.TenantId, s.BranchId, s.Status });
        builder.HasIndex(s => new { s.TenantId, s.TillId, s.Status });
        builder.HasIndex(s => new { s.TenantId, s.ShiftCode });
        builder.HasIndex(s => new { s.TenantId, s.BranchId, s.BusinessDate });
        builder.HasIndex(s => new { s.TenantId, s.BranchId })
            .HasDatabaseName("IX_ShiftZReports_OneActiveShiftPerBranch")
            .IsUnique()
            .HasFilter("\"Status\" IN (1, 3) AND NOT \"IsDeleted\"");
    }
}

public class ShiftCashMovementConfiguration : IEntityTypeConfiguration<ShiftCashMovement>
{
    public void Configure(EntityTypeBuilder<ShiftCashMovement> builder)
    {
        builder.ToTable("ShiftCashMovements");

        builder.Property(m => m.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(m => m.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(m => m.CreatedByName)
            .HasMaxLength(150);

        builder.HasIndex(m => new { m.TenantId, m.ShiftId });
        builder.HasIndex(m => new { m.TenantId, m.OccurredAtUtc });
    }
}

public class ExpenseVoucherConfiguration : IEntityTypeConfiguration<ExpenseVoucher>
{
    public void Configure(EntityTypeBuilder<ExpenseVoucher> builder)
    {
        builder.ToTable("ExpenseVouchers");
        builder.Property(e => e.Status).IsConcurrencyToken();

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

        builder.HasOne<HouseholdTaxSnapshot>()
            .WithMany()
            .HasForeignKey(e => e.HouseholdTaxSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.BranchId, e.ExpenseDate });
        builder.HasIndex(e => e.DueDate).HasFilter("\"DueDate\" IS NOT NULL AND \"IsDeleted\" = false");
        builder.HasIndex(e => new { e.TenantId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.ReferenceCode });
        builder.HasIndex(e => new { e.TenantId, e.Nature });
        builder.HasIndex(e => new { e.TenantId, e.HouseholdTaxSnapshotId });
    }
}
