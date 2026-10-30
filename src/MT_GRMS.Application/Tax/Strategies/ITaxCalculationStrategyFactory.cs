using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Factory interface to resolve the appropriate tax calculation strategy.
/// </summary>
public interface ITaxCalculationStrategyFactory
{
    /// <summary>
    /// Resolves the strategy for the requested tax calculation method.
    /// </summary>
    /// <param name="method">Tax calculation method enum.</param>
    /// <returns>Matching tax calculation strategy.</returns>
    ITaxCalculationStrategy Resolve(TaxCalculationMethod method);
}
