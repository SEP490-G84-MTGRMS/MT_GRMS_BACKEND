namespace MT_GRMS.Domain.Enums;

public enum PaymentMethod
{
    Cash = 1,
    VietQR = 2,
    BankTransfer = 3,
    Card = 4,
    Voucher = 5,
    Points = 6
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    PartiallyPaid = 2,
    Refunded = 3,
    Failed = 4
}

public enum LockMode
{
    Active = 0,
    SoftLockReadOnly = 1,
    HardLockSuspended = 2
}

public enum ExpiryRiskLevel
{
    NormalGreen = 0,
    WarningYellow = 1,
    HighOrange = 2,
    CriticalRed = 3
}
