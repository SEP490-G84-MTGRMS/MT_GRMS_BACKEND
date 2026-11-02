namespace MT_GRMS.Domain.Enums;

public enum EmployeeStatus
{
    Active = 1,
    Inactive = 2
}

public enum ScheduleStatus
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3
}

public enum ScheduleApprovalStatus
{
    None = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public enum PayslipStatus
{
    Unpaid = 1,
    Partial = 2,
    Paid = 3
}

