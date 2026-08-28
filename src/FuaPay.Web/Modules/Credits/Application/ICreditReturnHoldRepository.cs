using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public interface ICreditReturnHoldRepository
{
    Task<CreditReturnHold?> FindBySettlementReturnIdAsync(
        Guid settlementReturnId,
        CancellationToken cancellationToken = default);

    Task<CreditReturnHold?> FindBySettlementReturnIdForUpdateAsync(
        Guid settlementReturnId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        CreditReturnHold hold,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CreditReturnHold hold,
        CancellationToken cancellationToken = default);
}
