using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Reporting.Application;

public sealed record PaymentReconciliationExportRow(
    long? OrderNumber,
    string? PayId,
    Guid PaymentId,
    DateTimeOffset PaymentCreatedAt,
    DateTimeOffset? PaymentCompletedAt,
    long AmountMinorUnits,
    string Currency,
    PaymentPurposeType Purpose,
    string? JobNumber,
    string? ServiceUnit,
    Guid? FinancialDocumentId,
    string? FinancialDocumentNumber,
    SettlementReturnKind? ReturnKind,
    Guid? ReturnRequestId,
    Guid? SettlementReturnId,
    long? ReturnAmountMinorUnits,
    SettlementReturnState? ReturnState,
    Guid? ProviderAttemptId,
    SettlementReturnProviderOperation? ProviderOperation,
    SettlementReturnProviderAttemptState? ProviderAttemptState,
    DateTimeOffset? ReturnRequestedAt,
    DateTimeOffset? ReturnUpdatedAt,
    DateTimeOffset? AttemptStartedAt,
    DateTimeOffset? AttemptUpdatedAt,
    DateTimeOffset? AttemptFinishedAt);

public interface IPaymentReconciliationExportQueries
{
    Task<IReadOnlyList<PaymentReconciliationExportRow>> ListAsync(
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        int maximumRows,
        CancellationToken cancellationToken = default);
}
