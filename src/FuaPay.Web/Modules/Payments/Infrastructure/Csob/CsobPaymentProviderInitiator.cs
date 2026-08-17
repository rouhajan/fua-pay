using System.Globalization;

using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentProviderInitiator :
    IPaymentProviderInitiator
{
    private readonly ICsobGatewayClient _client;
    private readonly CsobGatewayAvailability _availability;

    public CsobPaymentProviderInitiator(
        ICsobGatewayClient client,
        CsobGatewayAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(availability);
        _client = client;
        _availability = availability;
    }

    public PaymentProvider Provider => PaymentProvider.Csob;

    public void EnsureAvailable() => _availability.EnsureEnabled();

    public async Task<PaymentProviderInitializationResult> InitializeAsync(
        PaymentProviderInitializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAvailable();

        if (request.Provider != Provider)
        {
            throw new InvalidOperationException(
                "ČSOB provider obdržel platbu určenou jinému poskytovateli.");
        }

        var itemName = request.PurposeType switch
        {
            PaymentPurposeType.CreditTopUp => "Dobití kreditu",
            PaymentPurposeType.Job => "Úhrada zakázky",
            _ => throw new InvalidOperationException(
                "ČSOB provider obdržel neznámý účel platby.")
        };

        var result = await _client.InitializeAsync(
            new CsobPaymentInit(
                request.OrderNumber.ToString(CultureInfo.InvariantCulture),
                request.Amount.MinorUnits,
                [new CsobPaymentCartItem(
                    itemName,
                    1,
                    request.Amount.MinorUnits)],
                request.CorrelationData),
            cancellationToken);

        var initialization = new PaymentProviderInitializationResult(
            Provider,
            result.PayId,
            result.ProcessUri);

        if (result.ResultCode != 0 || result.PaymentStatus != 1)
        {
            throw new PaymentProviderInitializationUncertainException(
                initialization,
                "Podepsaná odpověď payment/init obsahuje payId, ale " +
                "nepotvrdila očekávaný počáteční stav 1; payId se musí " +
                "uchovat a payment/init se nesmí opakovat.");
        }

        return initialization;
    }

    public async Task VerifyAsync(
        PaymentProviderInitializationResult candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        EnsureAvailable();

        if (candidate.Provider != Provider)
        {
            throw new InvalidOperationException(
                "ČSOB provider obdržel kandidáta určeného jinému poskytovateli.");
        }

        var status = await _client.GetStatusAsync(
            candidate.ProviderReference,
            cancellationToken);

        if (status.ResultCode != 0 || status.PaymentStatus != 1)
        {
            throw new CsobGatewayException(
                "Bezprostřední payment/status po payment/init nepotvrdilo " +
                "očekávaný pre-process stav 1; známé payId bude zpracováno " +
                "pouze konzervativní reconciliation cestou.",
                status.ResultCode);
        }
    }
}
