using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.System;

namespace MT_GRMS.Application.Notifications;

public static class BusinessNotificationTypes
{
    public const string ExpenseApprovalRequired = "EXPENSE_APPROVAL_REQUIRED";
    public const string ExpensePaymentFailed = "EXPENSE_PAYMENT_FAILED";
    public const string ExpensePaymentDue = "EXPENSE_PAYMENT_DUE";
    public const string CustomerDebtSettled = "CUSTOMER_DEBT_SETTLED";
    public const string CustomerDebtLimitExceeded = "CUSTOMER_DEBT_LIMIT_EXCEEDED";
    public const string WeeklyRevenueDrop = "WEEKLY_REVENUE_DROP";
    public const string TaxFilingReminder = "TAX_FILING_REMINDER";
    public const string TransferDispatchRequired = "TRANSFER_DISPATCH_REQUIRED";
    public const string TransferReceiveRequired = "TRANSFER_RECEIVE_REQUIRED";
    public const string TransferReceiptDiscrepancy = "TRANSFER_RECEIPT_DISCREPANCY";
    public const string CashbookReconciliationDiscrepancy = "CASHBOOK_RECONCILIATION_DISCREPANCY";
    public const string ShiftDiscrepancy = "SHIFT_DISCREPANCY";
    public const string TaxSnapshotFinalized = "TAX_SNAPSHOT_FINALIZED";
    public const string TaxPaymentRecorded = "TAX_PAYMENT_RECORDED";
}

public sealed record ExpenseReminderSource(Guid VoucherId, DateOnly DueDate);

public sealed record BusinessNotification(
    string Type,
    string Title,
    string Message,
    string OwnerUrl,
    Guid? StaffBranchId = null,
    string? StaffUrl = null,
    bool IncludeOwners = true,
    ExpenseReminderSource? ExpenseReminder = null,
    Guid? TaxFilingSnapshotId = null);

public sealed class NotificationOutbox(IRepository<OutboxMessage> messages, IConfiguration? configuration = null)
{
    public const string MessageType = "BusinessNotification";
    public const string ExpenseReminderKeyPrefix = "notification:expense-due:";

    public bool IsEnabled(string type) =>
        !bool.TryParse(configuration?[$"Notifications:BusinessEvents:{type}"], out var enabled) || enabled;

    public static string BuildKey(string type, Guid sourceId) => $"notification:{type}:{sourceId:N}";

    // The calling service saves the event in the same transaction as its business change.
    public async Task EnqueueAsync(string tenantId, Guid sourceId, BusinessNotification notification, CancellationToken ct)
    {
        if (!IsEnabled(notification.Type)) return;

        await messages.AddAsync(new OutboxMessage
        {
            TenantId = tenantId,
            Type = MessageType,
            IdempotencyKey = notification.ExpenseReminder == null
                ? BuildKey(notification.Type, sourceId)
                : ExpenseReminderKeyPrefix + sourceId,
            PayloadJson = JsonSerializer.Serialize(notification)
        }, ct);
    }

    public Task EnqueueDebtThresholdAsync(Customer customer, Guid orderId, decimal increase, CancellationToken ct)
    {
        if (!decimal.TryParse(configuration?[$"Notifications:OperatingRules:{customer.TenantId}:CustomerDebtLimit"],
                NumberStyles.Number, CultureInfo.InvariantCulture, out var limit)
            || limit <= 0 || increase <= 0 || customer.TotalDebt <= limit || customer.TotalDebt - increase > limit)
            return Task.CompletedTask;

        return EnqueueAsync(customer.TenantId, orderId, new BusinessNotification(
            BusinessNotificationTypes.CustomerDebtLimitExceeded, "Dư nợ khách hàng vượt ngưỡng cảnh báo",
            $"Khách hàng {customer.CustomerCode} có dư nợ {customer.TotalDebt:N0} đ, vượt ngưỡng {limit:N0} đ.",
            $"/store-owner/customers?search={Uri.EscapeDataString(customer.CustomerCode)}"), ct);
    }

    public Task EnqueueTransferAsync(Transfer transfer, string type, string title, Guid branchId, bool includeOwners, CancellationToken ct) =>
        EnqueueAsync(transfer.TenantId, transfer.Id, new BusinessNotification(
            type, title, $"Phiếu điều chuyển {transfer.TransferCode}.",
            $"/store-owner/transfers/{transfer.Id}", branchId, $"/employee/transfers/{transfer.Id}", includeOwners), ct);

    public Task EnqueueShiftDiscrepancyAsync(ShiftZReport shift, decimal discrepancy, CancellationToken ct)
    {
        var direction = discrepancy > 0 ? "thừa" : "thiếu";
        return EnqueueAsync(shift.TenantId, shift.Id, new BusinessNotification(
            BusinessNotificationTypes.ShiftDiscrepancy, "Chốt ca có chênh lệch tiền mặt",
            $"Ca {shift.ShiftCode} {direction} {Math.Abs(discrepancy):N0} đ.",
            $"/store-owner/shifts?branchId={shift.BranchId}&shiftId={shift.Id}"), ct);
    }
}
