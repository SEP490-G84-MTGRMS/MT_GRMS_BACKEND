namespace MT_GRMS.Application.PurchaseOrders.Dispatch;

public static class EmailSendModes
{
    public const string SendNow = "SEND_NOW";
    public const string ScheduledSend = "SCHEDULED_SEND";
}

public static class PackageEmailStatuses
{
    public const string Pending = "PENDING";
    public const string Scheduled = "SCHEDULED";
    public const string Sending = "SENDING";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

public static class PackageGenerationStatuses
{
    public const string Pending = "PENDING";
    public const string Generating = "GENERATING";
    public const string Generated = "GENERATED";
    public const string Failed = "FAILED";
}

public static class OutboxMessageTypes
{
    public const string SupplierOrderPackageProcess = "SupplierOrderPackageProcess";
}

public static class OutboxStatuses
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Processed = "PROCESSED";
    public const string Failed = "FAILED";
    public const string Dead = "DEAD";
}
