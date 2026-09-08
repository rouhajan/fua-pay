using System.Globalization;

using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentProviderInitiator :
    IPaymentProviderInitiator
{
    private readonly ICsobGatewayClient _client;
    private readonly CsobGatewayAvailability _availability;
    private readonly CsobGatewayConfiguration _configuration;

    public CsobPaymentProviderInitiator(
        ICsobGatewayClient client,
        CsobGatewayAvailability availability,
        CsobGatewayConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(configuration);
        _client = client;
        _availability = availability;
        _configuration = configuration;
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

    public Uri? ResolveTrustedProcessUri(
        PaymentProvider provider,
        string? providerReference,
        string? processUri)
    {
        if (
            provider != Provider ||
            string.IsNullOrWhiteSpace(providerReference) ||
            string.IsNullOrWhiteSpace(processUri) ||
            processUri.Length > PaymentInitiation.MaximumProcessUriLength ||
            !string.Equals(processUri, processUri.Trim(), StringComparison.Ordinal) ||
            processUri.Contains('\\') ||
            processUri.Any(char.IsControl))
        {
            return null;
        }

        string payId;

        try
        {
            payId = CsobPayId.RequireCanonical(providerReference);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (
            !Uri.TryCreate(processUri, UriKind.Absolute, out var candidate) ||
            !candidate.IsAbsoluteUri ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            candidate.UserInfo.Length != 0 ||
            candidate.Query.Length != 0 ||
            candidate.Fragment.Length != 0 ||
            !string.Equals(
                processUri,
                candidate.AbsoluteUri,
                StringComparison.Ordinal) ||
            !HasExpectedOrigin(candidate) ||
            !HasExpectedPath(candidate, payId))
        {
            return null;
        }

        return candidate;
    }

    private bool HasExpectedOrigin(Uri candidate) =>
        string.Equals(
            candidate.Scheme,
            _configuration.ApiBaseUri.Scheme,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            candidate.IdnHost,
            _configuration.ApiBaseUri.IdnHost,
            StringComparison.OrdinalIgnoreCase) &&
        candidate.Port == _configuration.ApiBaseUri.Port;

    private bool HasExpectedPath(Uri candidate, string payId)
    {
        var segments = candidate.AbsolutePath.Split(
            '/',
            StringSplitOptions.None);

        if (
            segments.Length != 9 ||
            segments[0].Length != 0 ||
            segments[1] != "api" ||
            segments[2] != "v1.9" ||
            segments[3] != "payment" ||
            segments[4] != "process" ||
            segments[5] != Uri.EscapeDataString(_configuration.MerchantId) ||
            segments[6] != Uri.EscapeDataString(payId) ||
            segments[7].Length != 14 ||
            !segments[7].All(char.IsAsciiDigit) ||
            segments[8].Length == 0)
        {
            return false;
        }

        return true;
    }
}
