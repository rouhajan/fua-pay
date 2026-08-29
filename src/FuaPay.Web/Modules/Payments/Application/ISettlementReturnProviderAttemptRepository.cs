using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public interface ISettlementReturnProviderAttemptRepository
{
    Task<SettlementReturnProviderAttempt?> FindByIdAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default);

    Task<SettlementReturnProviderAttempt?> FindActiveBySettlementReturnIdAsync(
        Guid settlementReturnId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettlementReturnProviderAttempt>>
        ListBySettlementReturnIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default);

    Task AddAsync(
        SettlementReturnProviderAttempt attempt,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        SettlementReturnProviderAttempt attempt,
        CancellationToken cancellationToken = default);
}
