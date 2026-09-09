namespace FuaPay.Web.Modules.Payments.Application;

public sealed record CardJobSettlementReturnCommand(
    Guid OperationId,
    Guid OriginalPaymentId,
    Guid AdministratorUserId,
    string Reason);

public enum CardJobSettlementReturnOutcome
{
    Unknown = 0,
    Confirmed = 1,
    RequiresAttention = 2,
    ReverseRejected = 3
}

public sealed record CardJobSettlementReturnResult(
    Guid SettlementReturnId,
    Guid ProviderAttemptId,
    CardJobSettlementReturnOutcome Outcome,
    bool ReverseRequestSent);

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
