using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record SettlementReturnAdministrationItem(
    Guid SettlementReturnId,
    Guid RequestId,
    SettlementReturnKind Kind,
    Guid OriginalPaymentId,
    SettlementReturnState State,
    string Reason,
    Guid? ProviderAttemptId,
    PaymentProvider? Provider,
    SettlementReturnProviderOperation? ProviderOperation,
    SettlementReturnProviderAttemptState? ProviderAttemptState,
    string? ProviderAttemptDiagnostic)
{
    private bool HasExpectedProviderAttempt =>
        ProviderAttemptId.HasValue &&
        ProviderAttemptId != Guid.Empty &&
        Provider == PaymentProvider.Csob &&
        (ProviderOperation != SettlementReturnProviderOperation.Reverse ||
         ProviderAttemptId == RequestId);

    public bool CanRecoverProviderAttempt =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedProviderAttempt &&
        (State is
            SettlementReturnState.InProgress or
            SettlementReturnState.RequiresAttention) &&
        (ProviderAttemptState is
            SettlementReturnProviderAttemptState.InProgress or
            SettlementReturnProviderAttemptState.Uncertain);

    public bool IsCompletedReverse =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedProviderAttempt &&
        State == SettlementReturnState.Completed &&
        ProviderOperation == SettlementReturnProviderOperation.Reverse &&
        ProviderAttemptState ==
            SettlementReturnProviderAttemptState.Confirmed;

    public bool IsCompletedRefund =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedProviderAttempt &&
        State == SettlementReturnState.Completed &&
        ProviderOperation == SettlementReturnProviderOperation.Refund &&
        ProviderAttemptState ==
            SettlementReturnProviderAttemptState.Confirmed;

    public bool IsRefundProcessing =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedProviderAttempt &&
        ProviderOperation == SettlementReturnProviderOperation.Refund &&
        State is
            SettlementReturnState.InProgress or
            SettlementReturnState.RequiresAttention &&
        (ProviderAttemptState ==
            SettlementReturnProviderAttemptState.InProgress ||
         (ProviderAttemptState ==
            SettlementReturnProviderAttemptState.Uncertain &&
          string.Equals(
              ProviderAttemptDiagnostic,
              CardJobSettlementReturnDiagnostics.RefundProcessing,
              StringComparison.Ordinal)));

    public bool IsPreExistingProviderRefund =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedProviderAttempt &&
        ProviderOperation == SettlementReturnProviderOperation.Reverse &&
        State == SettlementReturnState.RequiresAttention &&
        ProviderAttemptState ==
            SettlementReturnProviderAttemptState.Rejected &&
        (string.Equals(
             ProviderAttemptDiagnostic,
             CardJobSettlementReturnDiagnostics.PreExistingRefundProcessing,
             StringComparison.Ordinal) ||
         string.Equals(
             ProviderAttemptDiagnostic,
             CardJobSettlementReturnDiagnostics.PreExistingReturned,
             StringComparison.Ordinal));
}

public interface ISettlementReturnQueries
{
    Task<IReadOnlyDictionary<Guid, SettlementReturnAdministrationItem>>
        FindByOriginalPaymentIdsAsync(
            IEnumerable<Guid> originalPaymentIds,
            CancellationToken cancellationToken = default);
}
