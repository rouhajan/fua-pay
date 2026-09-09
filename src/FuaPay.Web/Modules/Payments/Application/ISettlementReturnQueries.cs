using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record SettlementReturnAdministrationItem(
    Guid SettlementReturnId,
    Guid RequestId,
    SettlementReturnKind Kind,
    Guid OriginalPaymentId,
    SettlementReturnState State,
    string Reason,
    Guid? ReverseAttemptId,
    PaymentProvider? ReverseAttemptProvider,
    SettlementReturnProviderAttemptState? ReverseAttemptState)
{
    private bool HasExpectedReverseAttempt =>
        ReverseAttemptId == RequestId &&
        ReverseAttemptProvider == PaymentProvider.Csob;

    public bool CanRecoverReverse =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedReverseAttempt &&
        (State is
            SettlementReturnState.InProgress or
            SettlementReturnState.RequiresAttention) &&
        (ReverseAttemptState is
            SettlementReturnProviderAttemptState.InProgress or
            SettlementReturnProviderAttemptState.Uncertain);

    public bool IsCompletedReverse =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedReverseAttempt &&
        State == SettlementReturnState.Completed &&
        ReverseAttemptState ==
            SettlementReturnProviderAttemptState.Confirmed;

    public bool IsRejectedReverse =>
        Kind == SettlementReturnKind.CardJob &&
        HasExpectedReverseAttempt &&
        State == SettlementReturnState.RequiresAttention &&
        ReverseAttemptState ==
            SettlementReturnProviderAttemptState.Rejected;
}

public interface ISettlementReturnQueries
{
    Task<IReadOnlyDictionary<Guid, SettlementReturnAdministrationItem>>
        FindByOriginalPaymentIdsAsync(
            IEnumerable<Guid> originalPaymentIds,
            CancellationToken cancellationToken = default);
}
