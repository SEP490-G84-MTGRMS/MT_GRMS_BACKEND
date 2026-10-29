using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Strategy contract for calculating household business taxes.
/// Implementations must be pure logic without direct database or I/O calls.
/// </summary>
public interface ITaxCalculationStrategy
{
    /// <summary>
    /// Gets the calculation method supported by this strategy.
    /// </summary>
    TaxCalculationMethod Method { get; }

    /// <summary>
    /// Computes VAT and PIT amounts given the immutable input context.
    /// </summary>
    /// <param name="context">The tax calculation context.</param>
    /// <returns>The computed tax calculation result.</returns>
    TaxCalculationResult Calculate(TaxCalculationContext context);
}
