using System.Buffers;
using System.Globalization;
using System.Text;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public static class CsobTextToSign
{
    public static string Echo(
        string merchantId,
        string dttm)
    {
        return string.Join(
            '|',
            RequireValue(merchantId, nameof(merchantId)),
            RequireDttm(dttm));
    }

    public static string PaymentInit(
        string merchantId,
        string orderNumber,
        string dttm,
        long totalAmountMinorUnits,
        Uri returnUri,
        IReadOnlyList<CsobPaymentCartItem> cart,
        string merchantData,
        int ttlSeconds)
    {
        ArgumentNullException.ThrowIfNull(returnUri);
        ArgumentNullException.ThrowIfNull(cart);

        var values = new List<string>
        {
            RequireValue(merchantId, nameof(merchantId)),
            NormalizeOrderNumber(orderNumber),
            RequireDttm(dttm),
            "payment",
            "card",
            RequirePositive(totalAmountMinorUnits, nameof(totalAmountMinorUnits)),
            "CZK",
            "true",
            ValidateReturnUri(returnUri),
            "POST"
        };

        var normalizedCart = NormalizeCart(
            cart,
            totalAmountMinorUnits);

        foreach (var item in normalizedCart)
        {
            values.Add(item.Name);
            values.Add(item.Quantity.ToString(CultureInfo.InvariantCulture));
            values.Add(item.AmountMinorUnits.ToString(CultureInfo.InvariantCulture));

            if (item.Description is not null)
            {
                values.Add(item.Description);
            }
        }

        values.Add(NormalizeMerchantData(merchantData));
        values.Add("cs");
        values.Add(ValidateTtl(ttlSeconds));

        return string.Join('|', values);
    }

    public static string PaymentStatus(
        string merchantId,
        string payId,
        string dttm) =>
        PaymentReferenceOperation(merchantId, payId, dttm);

    public static string PaymentProcess(
        string merchantId,
        string payId,
        string dttm) =>
        PaymentReferenceOperation(merchantId, payId, dttm);

    internal static string PaymentInitResponse(
        CsobGatewayResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var values = BaseResponse(response);

        if (!string.IsNullOrEmpty(response.StatusDetail))
        {
            values.Add(response.StatusDetail);
        }

        return string.Join('|', values);
    }

    internal static string EchoResponse(CsobGatewayResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return string.Join(
            '|',
            RequireExactDttm(response.Dttm),
            response.ResultCode.ToString(CultureInfo.InvariantCulture),
            RequireExactValue(
                response.ResultMessage,
                nameof(response.ResultMessage)));
    }

    internal static string PaymentStatusResponse(
        CsobGatewayResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var values = BaseResponse(response);

        if (!string.IsNullOrEmpty(response.AuthCode))
        {
            values.Add(response.AuthCode);
        }

        if (!string.IsNullOrEmpty(response.StatusDetail))
        {
            values.Add(response.StatusDetail);
        }

        return string.Join('|', values);
    }

    private static string PaymentReferenceOperation(
        string merchantId,
        string payId,
        string dttm)
    {
        return string.Join(
            '|',
            RequireValue(merchantId, nameof(merchantId)),
            RequireValue(payId, nameof(payId)),
            RequireDttm(dttm));
    }

    private static List<string> BaseResponse(
        CsobGatewayResponse response)
    {
        var values = new List<string>
        {
            RequireExactValue(response.PayId, nameof(response.PayId)),
            RequireExactDttm(response.Dttm),
            response.ResultCode.ToString(CultureInfo.InvariantCulture),
            RequireExactValue(
                response.ResultMessage,
                nameof(response.ResultMessage))
        };

        if (response.PaymentStatus.HasValue)
        {
            values.Add(
                response.PaymentStatus.Value.ToString(
                    CultureInfo.InvariantCulture));
        }

        return values;
    }

    internal static string NormalizeOrderNumber(string orderNumber) =>
        RequireDigits(orderNumber, 10, nameof(orderNumber));

    internal static string NormalizeMerchantData(string merchantData) =>
        RequireMerchantData(merchantData);

    internal static IReadOnlyList<CsobPaymentCartItem> NormalizeCart(
        IReadOnlyList<CsobPaymentCartItem> cart,
        long totalAmountMinorUnits)
    {
        ArgumentNullException.ThrowIfNull(cart);

        if (cart.Count is < 1 or > 2)
        {
            throw new ArgumentException(
                "Košík ČSOB musí obsahovat jednu nebo dvě položky.",
                nameof(cart));
        }

        var normalized = new CsobPaymentCartItem[cart.Count];
        long total = 0;

        for (var index = 0; index < cart.Count; index++)
        {
            var item = cart[index];
            var name = NormalizeCartText(
                item.Name,
                20,
                "Název položky košíku ČSOB");
            var description = string.IsNullOrWhiteSpace(item.Description)
                ? null
                : NormalizeCartText(
                    item.Description,
                    40,
                    "Popis položky košíku ČSOB");

            if (item.Quantity < 1 || item.AmountMinorUnits < 0)
            {
                throw new ArgumentException(
                    "Položka košíku ČSOB má neplatné množství nebo částku.",
                    nameof(cart));
            }

            total = checked(total + item.AmountMinorUnits);
            normalized[index] = new CsobPaymentCartItem(
                name,
                item.Quantity,
                item.AmountMinorUnits,
                description);
        }

        if (total != totalAmountMinorUnits)
        {
            throw new ArgumentException(
                "Součet položek košíku ČSOB neodpovídá celkové částce.",
                nameof(cart));
        }

        return normalized;
    }

    private static string NormalizeCartText(
        string? value,
        int maxLength,
        string fieldName)
    {
        var normalized = RequireValue(value, fieldName);

        var remaining = normalized.AsSpan();
        var characterCount = 0;

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(
                remaining,
                out var rune,
                out var consumed);

            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    $"{fieldName} obsahuje neplatnou Unicode hodnotu.",
                    fieldName);
            }

            var category = Rune.GetUnicodeCategory(rune);

            if (
                rune.Value == '|' ||
                category is
                    UnicodeCategory.Control or
                    UnicodeCategory.Format or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator)
            {
                throw new ArgumentException(
                    $"{fieldName} obsahuje znak nepovolený pro podpis ČSOB.",
                    fieldName);
            }

            characterCount++;
            remaining = remaining[consumed..];
        }

        if (characterCount > maxLength)
        {
            throw new ArgumentException(
                $"{fieldName} smí mít nejvýše {maxLength} znaků.",
                fieldName);
        }

        return normalized;
    }

    private static string ValidateReturnUri(Uri returnUri)
    {
        if (
            !returnUri.IsAbsoluteUri ||
            returnUri.Scheme != Uri.UriSchemeHttps ||
            returnUri.AbsoluteUri.Length > 300)
        {
            throw new ArgumentException(
                "Návratová URL ČSOB musí být absolutní HTTPS adresa o délce nejvýše 300 znaků.",
                nameof(returnUri));
        }

        return returnUri.AbsoluteUri;
    }

    private static string RequireMerchantData(string value)
    {
        var normalized = RequireValue(value, nameof(value));

        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Merchant data ČSOB nesmí obsahovat bílé znaky.",
                nameof(value));
        }

        if (normalized.Length > 255)
        {
            throw new ArgumentException(
                "Merchant data ČSOB smí mít po Base64 kódování nejvýše 255 znaků.",
                nameof(value));
        }

        try
        {
            _ = Convert.FromBase64String(normalized);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Merchant data ČSOB musí být platná Base64 hodnota.",
                nameof(value),
                exception);
        }

        return normalized;
    }

    private static string RequireExactDttm(string? value)
    {
        var exact = RequireExactValue(value, "dttm");

        if (
            exact.Length != 14 ||
            exact.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException(
                "Čas ČSOB musí mít přesný formát yyyyMMddHHmmss.",
                "dttm");
        }

        return exact;
    }

    private static string RequireExactValue(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Hodnota nesmí být prázdná.",
                parameterName);
        }

        return value;
    }

    private static string RequireDttm(string? value)
    {
        var normalized = RequireDigits(value, 14, "dttm");

        if (normalized.Length != 14)
        {
            throw new ArgumentException(
                "Čas ČSOB musí mít formát yyyyMMddHHmmss.",
                "dttm");
        }

        return normalized;
    }

    private static string RequireDigits(
        string? value,
        int maxLength,
        string parameterName)
    {
        var normalized = RequireValue(value, parameterName);

        if (
            normalized.Length > maxLength ||
            normalized.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException(
                "Hodnota musí obsahovat pouze číslice.",
                parameterName);
        }

        return normalized;
    }

    private static string RequireValue(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Hodnota nesmí být prázdná.",
                parameterName);
        }

        return value.Trim();
    }

    private static string RequirePositive(
        long value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string ValidateTtl(int ttlSeconds)
    {
        if (ttlSeconds is < 300 or > 1800)
        {
            throw new ArgumentOutOfRangeException(nameof(ttlSeconds));
        }

        return ttlSeconds.ToString(CultureInfo.InvariantCulture);
    }
}
