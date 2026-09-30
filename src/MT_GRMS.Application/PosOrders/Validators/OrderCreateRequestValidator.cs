using FluentValidation;
using MT_GRMS.Application.PosOrders.Dtos;

namespace MT_GRMS.Application.PosOrders.Validators;

public class OrderCreateRequestValidator : AbstractValidator<OrderCreateRequest>
{
    public OrderCreateRequestValidator()
    {
    }
}
