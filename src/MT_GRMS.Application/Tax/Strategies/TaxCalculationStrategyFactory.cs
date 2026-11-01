using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Tax.Strategies;

/// <summary>
/// Factory to resolve the appropriate tax calculation strategy based on calculation method.
/// </summary>
public class TaxCalculationStrategyFactory : ITaxCalculationStrategyFactory
{
    private readonly IEnumerable<ITaxCalculationStrategy> _strategies;

    public TaxCalculationStrategyFactory(IEnumerable<ITaxCalculationStrategy> strategies)
    {
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
    }

    public ITaxCalculationStrategy Resolve(TaxCalculationMethod method)
    {
        var strategy = _strategies.FirstOrDefault(s => s.Method == method);
        if (strategy == null)
        {
            throw new NotSupportedException($"Tax calculation strategy for method '{method}' is not registered.");
        }
        return strategy;
    }
}
