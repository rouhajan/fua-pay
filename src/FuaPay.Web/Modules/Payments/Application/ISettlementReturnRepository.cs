using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public interface ISettlementReturnRepository
{
    Task<SettlementReturn?> FindByIdAsync(
        Guid settlementReturnId,
        CancellationToken cancellationToken = default);

    Task<SettlementReturn?> FindByRequestIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<SettlementReturn?> FindByOriginalPaymentIdAsync(
        Guid originalPaymentId,
        CancellationToken cancellationToken = default);

    Task<SettlementReturn?> FindByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        SettlementReturn settlementReturn,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        SettlementReturn settlementReturn,
        CancellationToken cancellationToken = default);
}
