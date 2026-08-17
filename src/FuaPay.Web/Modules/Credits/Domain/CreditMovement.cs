using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Credits.Domain;

public sealed record CreditMovement(
    Guid OperationId,
    CreditMovementType Type,
    Money Amount,
    Money BalanceAfter,
    DateTimeOffset RecordedAt,
    string Description);
