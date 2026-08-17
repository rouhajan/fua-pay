namespace FuaPay.Web.Modules.Payments.Application;

public interface IPaymentSettlementService
{
    Task<bool> CompleteAsync(
        VerifiedPaymentConfirmation confirmation,
        CancellationToken cancellationToken = default);
}
