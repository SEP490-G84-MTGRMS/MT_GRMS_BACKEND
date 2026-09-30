using FluentValidation;
using MT_GRMS.Application.Batches.Dtos;

namespace MT_GRMS.Application.Batches.Validators;

public class BatchCreateRequestValidator : AbstractValidator<BatchCreateRequest>
{
    public BatchCreateRequestValidator()
    {
    }
}
