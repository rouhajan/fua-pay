using System.Globalization;

using Microsoft.Extensions.Primitives;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed record CsobVerifiedPaymentReturn(
    string PayId,
    string Dttm,
    int ResultCode,
    string ResultMessage,
    int PaymentStatus,
    string? AuthCode,
    string? MerchantData,
    string? StatusDetail,
    string TextToSign,
    string Signature)
{
    public bool IsExpired => ResultCode == 130 && PaymentStatus == 6;
}

public sealed class CsobPaymentReturnVerifier
{
    internal static readonly TimeSpan MaximumClockSkew =
        TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> AllowedParameters =
        new(StringComparer.Ordinal)
        {
            "payId",
            "dttm",
            "resultCode",
            "resultMessage",
            "paymentStatus",
            "authCode",
            "merchantData",
            "statusDetail",
            "signature"
        };

    private readonly ICsobGatewaySignature _signature;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _gatewayTimeZone;

    public CsobPaymentReturnVerifier(
        ICsobGatewaySignature signature,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _signature = signature;
        _timeProvider = timeProvider;
        _gatewayTimeZone = ResolveGatewayTimeZone();
    }

    public bool TryVerify(
        IEnumerable<KeyValuePair<string, StringValues>> parameters,
        out CsobVerifiedPaymentReturn? verifiedReturn)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        verifiedReturn = null;

        Dictionary<string, string> values;

        try
        {
            values = ParseParameters(parameters);
        }
        catch (ArgumentException)
        {
            return false;
        }

        try
        {
            var payId = CsobPayId.RequireCanonical(
                Require(values, "payId"));
            var dttm = RequireExactDttm(Require(values, "dttm"));
            var resultCode = RequireCanonicalInteger(
                Require(values, "resultCode"),
                "resultCode");
            var resultMessage = RequireBounded(
                Require(values, "resultMessage"),
                "resultMessage",
                1024);
            var paymentStatus = RequireCanonicalInteger(
                Require(values, "paymentStatus"),
                "paymentStatus");
            var authCode = OptionalBounded(values, "authCode", 64);
            var merchantData = OptionalMerchantData(values);
            var statusDetail = OptionalBounded(
                values,
                "statusDetail",
                1024);
            var signature = RequireBounded(
                Require(values, "signature"),
                "signature",
                1024);

            if (signature.Any(char.IsWhiteSpace))
            {
                return false;
            }

            var signedValues = new List<string>
            {
                payId,
                dttm,
                resultCode.ToString(CultureInfo.InvariantCulture),
                resultMessage,
                paymentStatus.ToString(CultureInfo.InvariantCulture)
            };

            AddOptional(signedValues, authCode);
            AddOptional(signedValues, merchantData);
            AddOptional(signedValues, statusDetail);

            var textToSign = string.Join('|', signedValues);

            if (
                textToSign.Length >
                    CsobVerifiedReturnEvidence.MaximumTextToSignLength ||
                !_signature.Verify(textToSign, signature) ||
                !IsFresh(dttm, _timeProvider.GetUtcNow()))
            {
                return false;
            }

            verifiedReturn = new CsobVerifiedPaymentReturn(
                payId,
                dttm,
                resultCode,
                resultMessage,
                paymentStatus,
                authCode,
                merchantData,
                statusDetail,
                textToSign,
                signature);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> ParseParameters(
        IEnumerable<KeyValuePair<string, StringValues>> parameters)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            if (
                !AllowedParameters.Contains(parameter.Key) ||
                parameter.Value.Count != 1 ||
                !parsed.TryAdd(parameter.Key, parameter.Value[0] ?? string.Empty))
            {
                throw new ArgumentException(
                    "Návrat ČSOB obsahuje neznámý nebo duplicitní parametr.",
                    nameof(parameters));
            }
        }

        return parsed;
    }

    private bool IsFresh(string dttm, DateTimeOffset receivedAt)
    {
        if (
            !DateTime.TryParseExact(
                dttm,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTime))
        {
            return false;
        }

        localTime = DateTime.SpecifyKind(
            localTime,
            DateTimeKind.Unspecified);

        if (
            _gatewayTimeZone.IsInvalidTime(localTime) ||
            _gatewayTimeZone.IsAmbiguousTime(localTime))
        {
            return false;
        }

        var gatewayTime = new DateTimeOffset(
            localTime,
            _gatewayTimeZone.GetUtcOffset(localTime));

        return
            gatewayTime >= receivedAt - MaximumClockSkew &&
            gatewayTime <= receivedAt + MaximumClockSkew;
    }

    private static string Require(
        IReadOnlyDictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value))
        {
            throw new ArgumentException(
                $"Návrat ČSOB neobsahuje povinný parametr {name}.",
                name);
        }

        return value;
    }

    private static string RequireExactDttm(string value)
    {
        if (
            value.Length != 14 ||
            !value.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "Čas návratu ČSOB musí mít formát yyyyMMddHHmmss.",
                "dttm");
        }

        return value;
    }

    private static int RequireCanonicalInteger(
        string value,
        string name)
    {
        if (
            !int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0 ||
            !string.Equals(
                value,
                parsed.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Parametr {name} návratu ČSOB není kanonické nezáporné číslo.",
                name);
        }

        return parsed;
    }

    private static string RequireBounded(
        string value,
        string name,
        int maximumLength)
    {
        if (
            string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Parametr {name} návratu ČSOB má neplatnou délku.",
                name);
        }

        return value;
    }

    private static string? OptionalBounded(
        IReadOnlyDictionary<string, string> values,
        string name,
        int maximumLength)
    {
        return values.TryGetValue(name, out var value)
            ? RequireBounded(value, name, maximumLength)
            : null;
    }

    private static string? OptionalMerchantData(
        IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("merchantData", out var value))
        {
            return null;
        }

        if (
            value.Length > 255 ||
            value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Parametr merchantData návratu ČSOB není platná Base64 hodnota.",
                "merchantData");
        }

        try
        {
            _ = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Parametr merchantData návratu ČSOB není platná Base64 hodnota.",
                "merchantData",
                exception);
        }

        return value;
    }

    private static void AddOptional(
        ICollection<string> values,
        string? value)
    {
        if (value is not null)
        {
            values.Add(value);
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
}

public sealed record CsobVerifiedReturnEvidence(
    string Dttm,
    int ResultCode,
    int PaymentStatus,
    string TextToSign,
    string Signature,
    DateTimeOffset ObservedAt)
{
    public const int MaximumTextToSignLength = 4096;
    public const int MaximumSignatureLength = 1024;
}
