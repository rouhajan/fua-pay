namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentRequiresAttentionException :
    CsobGatewayException
{
    public CsobPaymentRequiresAttentionException(
        string message,
        int? gatewayPaymentStatus = null,
        int? resultCode = null,
        Exception? innerException = null)
        : base(
            message,
            resultCode: resultCode,
            innerException: innerException)
    {
        GatewayPaymentStatus = gatewayPaymentStatus;
    }

    public int? GatewayPaymentStatus { get; }
}
