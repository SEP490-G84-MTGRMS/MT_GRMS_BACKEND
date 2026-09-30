namespace MT_GRMS.Application.AiPipeline.Dtos;

public record AiBasketRecommendationRequest(
    Guid BranchId,
    string CustomerQuery,
    decimal? BudgetLimit
);

public record AiBasketRecommendationResponse(
    string RecommendationText,
    List<AiRecommendedItemDto> SuggestedItems,
    decimal EstimatedTotal
);

public record AiRecommendedItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    string Reason
);
