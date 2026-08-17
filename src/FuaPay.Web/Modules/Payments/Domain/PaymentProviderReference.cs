using System.Buffers;
using System.Globalization;
using System.Text;

namespace FuaPay.Web.Modules.Payments.Domain;

public static class PaymentProviderReference
{
    public const int MaxLength = 160;

    public static string Normalize(
        string value,
        string parameterName = "providerReference")
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        ValidateCharacters(value, parameterName);

        var normalized = value.Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Reference poskytovatele nesmí být prázdná.",
                parameterName);
        }

        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException(
                "Reference poskytovatele je příliš dlouhá.",
                parameterName);
        }

        return normalized;
    }

    private static void ValidateCharacters(
        string value,
        string parameterName)
    {
        var remaining = value.AsSpan();

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(
                remaining,
                out var rune,
                out var consumed);

            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "Reference poskytovatele obsahuje neplatné Unicode znaky.",
                    parameterName);
            }

            if (IsForbiddenCategory(
                Rune.GetUnicodeCategory(rune)))
            {
                throw new ArgumentException(
                    "Reference poskytovatele obsahuje nepovolené znaky.",
                    parameterName);
            }

            remaining = remaining[consumed..];
        }
    }

    private static bool IsForbiddenCategory(
        UnicodeCategory category)
    {
        return category is
            UnicodeCategory.Control or
            UnicodeCategory.Format or
            UnicodeCategory.LineSeparator or
            UnicodeCategory.ParagraphSeparator or
            UnicodeCategory.Surrogate;
    }
}
