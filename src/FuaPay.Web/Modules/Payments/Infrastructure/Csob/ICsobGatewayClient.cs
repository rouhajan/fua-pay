namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public interface ICsobGatewayClient
{
    Task<CsobEchoResult> EchoAsync(
        CancellationToken cancellationToken = default);

    Task<CsobPaymentInitResult> InitializeAsync(
        CsobPaymentInit payment,
        CancellationToken cancellationToken = default);

    Task<CsobPaymentStatusResult> GetStatusAsync(
        string payId,
        CancellationToken cancellationToken = default);
}
