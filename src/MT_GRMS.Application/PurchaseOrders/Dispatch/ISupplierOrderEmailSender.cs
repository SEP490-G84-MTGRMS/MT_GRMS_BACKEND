namespace MT_GRMS.Application.PurchaseOrders.Dispatch;

public interface ISupplierOrderEmailSender
{
    Task SendSupplierOrderAsync(SupplierOrderEmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SupplierOrderEmailMessage
{
    public string To { get; set; } = string.Empty;
    public IReadOnlyList<string> Cc { get; set; } = Array.Empty<string>();
    public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string AttachmentFileName { get; set; } = string.Empty;
    public byte[] AttachmentBytes { get; set; } = Array.Empty<byte>();
}
