namespace MT_GRMS.Application.Common;

public class SePaySettings
{
    public const string SectionName = "SePay";

    public string ApiKey { get; set; } = string.Empty;
    public string BankCode { get; set; } = "MBBank";
    public string BankName { get; set; } = "MB Bank (Ngân hàng Quân Đội)";
    public string AccountNumber { get; set; } = "190065228888";
    public string AccountName { get; set; } = "CONG TY TNHH PHAN MEM MT-GRMS";
}

