namespace FuaPay.Web.Modules.Payments.Application;

public sealed record CardJobSettlementReturnCommand(
    Guid OperationId,
    Guid OriginalPaymentId,
    Guid AdministratorUserId,
    string Reason);

public static class CardJobSettlementReturnDiagnostics
{
    public const string PreExistingRefundProcessing =
        "Signed CSOB evidence found paymentStatus 9 before FUA Pay refund.";

    public const string PreExistingReturned =
        "Signed CSOB evidence found paymentStatus 10 before FUA Pay refund.";

    public const string RefundProcessing =
        "Signed CSOB status proved paymentStatus 9; refund is processing.";
}

public enum CardJobSettlementReturnOutcome
{
    Unknown = 0,
    ReverseCompleted = 1,
    RefundProcessing = 2,
    RefundCompleted = 3,
    RequiresAttention = 4,
    PreExistingProviderRefund = 5
}

public sealed record CardJobSettlementReturnResult(
    Guid SettlementReturnId,
    Guid ProviderAttemptId,
    CardJobSettlementReturnOutcome Outcome,
    bool ReverseRequestSent,
    bool RefundRequestSent = false);

public interface ICardJobSettlementReturnService
{
    Task<CardJobSettlementReturnResult> ReturnAsync(
        CardJobSettlementReturnCommand command,
        CancellationToken cancellationToken = default);
}

internal sealed class UnavailableCardJobSettlementReturnService :
    ICardJobSettlementReturnService
{
    public Task<CardJobSettlementReturnResult> ReturnAsync(
        CardJobSettlementReturnCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        throw new CardJobSettlementReturnNotAvailableException();
    }
}
