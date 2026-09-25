namespace FuaPay.Web.Modules.Payments.Application;

public sealed record CardTopUpSettlementReturnCommand(
    Guid RequestId,
    Guid OriginalPaymentId,
    Guid AdministratorUserId,
    string Reason);

public enum CardTopUpSettlementReturnOutcome
{
    Unknown = 0,
    ReverseCompleted = 1,
    RefundProcessing = 2,
    RefundCompleted = 3,
    RequiresAttention = 4,
    PreExistingProviderRefund = 5,
    Rejected = 6
}

public sealed record CardTopUpSettlementReturnResult(
    Guid SettlementReturnId,
    Guid ProviderAttemptId,
    CardTopUpSettlementReturnOutcome Outcome,
    bool ReverseRequestSent,
    bool RefundRequestSent = false);

public interface ICardTopUpSettlementReturnService
{
    Task<CardTopUpSettlementReturnResult> ReturnAsync(
        CardTopUpSettlementReturnCommand command,
        CancellationToken cancellationToken = default);
}

internal sealed class UnavailableCardTopUpSettlementReturnService :
    ICardTopUpSettlementReturnService
{
    public Task<CardTopUpSettlementReturnResult> ReturnAsync(
        CardTopUpSettlementReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        throw new CardTopUpSettlementReturnNotAvailableException();
    }
}
