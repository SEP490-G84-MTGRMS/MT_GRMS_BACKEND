namespace MT_GRMS.Domain.Entities;

/// <summary>
/// Alias chuyển tiếp cho Sales.Order để duy trì tương thích ngược
/// </summary>
public class Order : Sales.Order
{
}

/// <summary>
/// Alias chuyển tiếp cho Sales.OrderDetail để duy trì tương thích ngược
/// </summary>
public class OrderItem : Sales.OrderDetail
{
}
