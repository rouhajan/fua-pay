using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobGatewayClient : ICsobGatewayClient
{
    private const string ApiVersion = "v1.9";

    private static readonly TimeSpan MaximumResponseClockSkew =
        TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly CsobGatewayConfiguration _configuration;
    private readonly CsobGatewayAvailability _availability;
    private readonly ICsobGatewaySignature _signature;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _gatewayTimeZone;

    public CsobGatewayClient(
        HttpClient httpClient,
        CsobGatewayConfiguration configuration,
        CsobGatewayAvailability availability,
        ICsobGatewaySignature signature,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
        _configuration = configuration;
        _availability = availability;
        _signature = signature;
        _timeProvider = timeProvider;
        _gatewayTimeZone = ResolveGatewayTimeZone();
    }

    public async Task<CsobEchoResult> EchoAsync(
        CancellationToken cancellationToken = default)
    {
        _availability.EnsureEnabled();
        var requestStartedAt = _timeProvider.GetUtcNow();
        var dttm = CreateDttm(requestStartedAt);
        var signature = _signature.Sign(
            CsobTextToSign.Echo(
                _configuration.MerchantId,
                dttm));
        var requestUri = string.Join(
            "/",
            $"api/{ApiVersion}/echo",
            Escape(_configuration.MerchantId),
            Escape(dttm),
            Escape(signature));

        using var response = await SendAsync(
            () => _httpClient.GetAsync(
                requestUri,
                cancellationToken),
            cancellationToken);
        var responseReceivedAt = _timeProvider.GetUtcNow();
        var gatewayResponse = await ReadVerifiedResponseAsync(
            response,
            CsobTextToSign.EchoResponse,
            requestStartedAt,
            responseReceivedAt,
            cancellationToken);

        return new CsobEchoResult(
            gatewayResponse.ResultCode,
            RequireResponseValue(
                gatewayResponse.ResultMessage,
                "Odpověď echo neobsahuje resultMessage."));
    }

    public async Task<CsobPaymentInitResult> InitializeAsync(
        CsobPaymentInit payment,
        CancellationToken cancellationToken = default)
    {
        _availability.EnsureEnabled();
        ArgumentNullException.ThrowIfNull(payment);

        var requestStartedAt = _timeProvider.GetUtcNow();
        var dttm = CreateDttm(requestStartedAt);
        var orderNumber = CsobTextToSign.NormalizeOrderNumber(
            payment.OrderNumber);
        var merchantData = CsobTextToSign.NormalizeMerchantData(
            payment.MerchantData);
        var normalizedCart = CsobTextToSign.NormalizeCart(
            payment.Cart,
            payment.TotalAmountMinorUnits);
        var textToSign = CsobTextToSign.PaymentInit(
            _configuration.MerchantId,
            orderNumber,
            dttm,
            payment.TotalAmountMinorUnits,
            _configuration.ReturnUri,
            normalizedCart,
            merchantData,
            _configuration.PaymentTtlSeconds);

        var request = new CsobPaymentInitRequest(
            _configuration.MerchantId,
            orderNumber,
            dttm,
            "payment",
            "card",
            payment.TotalAmountMinorUnits,
            "CZK",
            ClosePayment: true,
            _configuration.ReturnUri.AbsoluteUri,
            "POST",
            normalizedCart.Select(
                    item => new CsobPaymentCartItemRequest(
                        item.Name,
                        item.Quantity,
                        item.AmountMinorUnits,
                        item.Description))
                .ToArray(),
            merchantData,
            "cs",
            _configuration.PaymentTtlSeconds,
            _signature.Sign(textToSign));

        using var response = await SendAsync(
            () => _httpClient.PostAsJsonAsync(
                $"api/{ApiVersion}/payment/init",
                request,
                JsonOptions,
                cancellationToken),
            cancellationToken);
        var responseReceivedAt = _timeProvider.GetUtcNow();

        var gatewayResponse = await ReadVerifiedResponseAsync(
            response,
            CsobTextToSign.PaymentInitResponse,
            requestStartedAt,
            responseReceivedAt,
            cancellationToken);

        var payId = CsobPayId.RequireSigned(
            gatewayResponse.PayId,
            "Odpověď payment/init neobsahuje platné payId.");
        var paymentStatus = gatewayResponse.PaymentStatus
            ?? throw new CsobGatewayException(
                "Odpověď payment/init neobsahuje stav platby.");

        return new CsobPaymentInitResult(
            payId,
            paymentStatus,
            CreateProcessUri(payId),
            gatewayResponse.ResultCode,
            RequireResponseValue(
                gatewayResponse.ResultMessage,
                "Odpověď payment/init neobsahuje resultMessage."));
    }

    public async Task<CsobPaymentStatusResult> GetStatusAsync(
        string payId,
        CancellationToken cancellationToken = default)
    {
        _availability.EnsureEnabled();
        var normalizedPayId = CsobPayId.RequireGatewayInput(payId);
        var requestStartedAt = _timeProvider.GetUtcNow();
        var dttm = CreateDttm(requestStartedAt);
        var signature = _signature.Sign(
            CsobTextToSign.PaymentStatus(
                _configuration.MerchantId,
                normalizedPayId,
                dttm));

        var requestUri = string.Join(
            "/",
            $"api/{ApiVersion}/payment/status",
            Escape(_configuration.MerchantId),
            Escape(normalizedPayId),
            Escape(dttm),
            Escape(signature));

        using var response = await SendAsync(
            () => _httpClient.GetAsync(
                requestUri,
                cancellationToken),
            cancellationToken);
        var responseReceivedAt = _timeProvider.GetUtcNow();
        var gatewayResponse = await ReadVerifiedResponseAsync(
            response,
            CsobTextToSign.PaymentStatusResponse,
            requestStartedAt,
            responseReceivedAt,
            cancellationToken);

        var responsePayId = CsobPayId.RequireSigned(
            gatewayResponse.PayId,
            "Odpověď payment/status neobsahuje platné payId.");

        if (!string.Equals(
            responsePayId,
            normalizedPayId,
            StringComparison.Ordinal))
        {
            throw new CsobGatewayException(
                "Odpověď payment/status patří jiné platbě než požadované payId.");
        }

        return new CsobPaymentStatusResult(
            responsePayId,
            gatewayResponse.ResultCode,
            RequireResponseValue(
                gatewayResponse.ResultMessage,
                "Odpověď payment/status neobsahuje resultMessage."),
            gatewayResponse.PaymentStatus
                ?? throw new CsobGatewayException(
                    "Odpověď payment/status neobsahuje stav platby."),
            gatewayResponse.AuthCode,
            gatewayResponse.StatusDetail);
    }

    private Uri CreateProcessUri(string payId)
    {
        var dttm = CreateDttm(_timeProvider.GetUtcNow());
        var signature = _signature.Sign(
            CsobTextToSign.PaymentProcess(
                _configuration.MerchantId,
                payId,
                dttm));
        var relative = string.Join(
            "/",
            $"api/{ApiVersion}/payment/process",
            Escape(_configuration.MerchantId),
            Escape(payId),
            Escape(dttm),
            Escape(signature));

        return new Uri(_configuration.ApiBaseUri, relative);
    }

    private async Task<CsobGatewayResponse> ReadVerifiedResponseAsync(
        HttpResponseMessage response,
        Func<CsobGatewayResponse, string> createTextToSign,
        DateTimeOffset requestStartedAt,
        DateTimeOffset responseReceivedAt,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await TryReadErrorAsync(
                response,
                cancellationToken);

            var diagnostic = error?.ResultCode is int resultCode
                ? $" Neověřený diagnostický kód brány: {resultCode}."
                : string.Empty;

            throw new CsobGatewayException(
                "Volání platební brány ČSOB nebylo úspěšně zpracováno." +
                diagnostic,
                httpStatusCode: response.StatusCode);
        }

        CsobGatewayResponse gatewayResponse;

        try
        {
            gatewayResponse =
                await response.Content.ReadFromJsonAsync<CsobGatewayResponse>(
                    JsonOptions,
                    cancellationToken)
                ?? throw new CsobGatewayException(
                    "Platební brána ČSOB vrátila prázdnou odpověď.");
        }
        catch (Exception exception)
            when (exception is JsonException or NotSupportedException)
        {
            throw new CsobGatewayException(
                "Platební brána ČSOB vrátila neplatnou JSON odpověď.",
                innerException: exception);
        }

        var signature = gatewayResponse.Signature;
        string textToSign;

        try
        {
            textToSign = createTextToSign(gatewayResponse);
        }
        catch (ArgumentException exception)
        {
            throw new CsobGatewayException(
                "Podepsaná odpověď platební brány ČSOB má neplatnou strukturu.",
                innerException: exception);
        }

        if (!_signature.Verify(textToSign, signature ?? string.Empty))
        {
            throw new CsobGatewayException(
                "Podpis odpovědi platební brány ČSOB není platný.");
        }

        EnsureFreshResponse(
            gatewayResponse.Dttm,
            requestStartedAt,
            responseReceivedAt);

        return gatewayResponse;
    }

    private static async Task<CsobGatewayErrorResponse?> TryReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<CsobGatewayErrorResponse>(
                    JsonOptions,
                    cancellationToken);
        }
        catch (Exception exception)
            when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException exception)
        {
            throw new CsobGatewayException(
                "Platební brána ČSOB není dostupná.",
                innerException: exception);
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new CsobGatewayException(
                "Volání platební brány ČSOB překročilo povolený čas.",
                innerException: exception);
        }
    }

    private string CreateDttm(DateTimeOffset timestamp)
    {
        return TimeZoneInfo.ConvertTime(
                timestamp,
                _gatewayTimeZone)
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    }

    private void EnsureFreshResponse(
        string? value,
        DateTimeOffset requestStartedAt,
        DateTimeOffset responseReceivedAt)
    {
        if (
            value is null ||
            !DateTime.TryParseExact(
                value,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var gatewayLocalTime))
        {
            throw new CsobGatewayException(
                "Podepsaná odpověď ČSOB neobsahuje platný čas dttm.");
        }

        gatewayLocalTime = DateTime.SpecifyKind(
            gatewayLocalTime,
            DateTimeKind.Unspecified);

        if (_gatewayTimeZone.IsInvalidTime(gatewayLocalTime))
        {
            throw new CsobGatewayException(
                "Podepsaná odpověď ČSOB obsahuje neexistující lokální čas dttm.");
        }

        if (_gatewayTimeZone.IsAmbiguousTime(gatewayLocalTime))
        {
            throw new CsobGatewayException(
                "Podepsaná odpověď ČSOB obsahuje nejednoznačný lokální čas dttm.");
        }

        var offset = _gatewayTimeZone.GetUtcOffset(gatewayLocalTime);
        var earliestAccepted = requestStartedAt - MaximumResponseClockSkew;
        var latestAccepted = responseReceivedAt + MaximumResponseClockSkew;
        var responseTime = new DateTimeOffset(
            gatewayLocalTime,
            offset);
        var isWithinRequestWindow =
            responseTime >= earliestAccepted &&
            responseTime <= latestAccepted;

        if (!isWithinRequestWindow)
        {
            throw new CsobGatewayException(
                "Podepsaná odpověď ČSOB má dttm mimo časové okno konkrétního požadavku.");
        }
    }

    private static TimeZoneInfo ResolveGatewayTimeZone()
    {
        foreach (
            var identifier in
            new[] { "Europe/Prague", "Central Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(identifier);
            }
            catch (Exception exception)
                when (exception is
                    TimeZoneNotFoundException or
                    InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException(
            "Systém neobsahuje časovou zónu Europe/Prague potřebnou pro ČSOB eAPI.");
    }

    private static string Escape(string value) =>
        Uri.EscapeDataString(value);

    private static string RequireResponseValue(
        string? value,
        string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CsobGatewayException(errorMessage);
        }

        return value;
    }
}
