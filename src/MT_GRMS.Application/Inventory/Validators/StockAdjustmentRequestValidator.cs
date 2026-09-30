using FluentValidation;
using MT_GRMS.Application.Inventory.Dtos;

namespace MT_GRMS.Application.Inventory.Validators;

public class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
    }
}
