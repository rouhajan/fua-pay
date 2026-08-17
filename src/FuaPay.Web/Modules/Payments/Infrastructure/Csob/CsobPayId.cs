using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

internal static class CsobPayId
{
    internal const int MaximumLength = 15;

    public static string RequireSigned(
        string? value,
        string errorMessage)
    {
        if (value is null)
        {
            throw new CsobGatewayException(errorMessage);
        }

        string normalized;

        try
        {
            normalized = PaymentProviderReference.Normalize(
                value,
                nameof(value));
        }
        catch (ArgumentException exception)
        {
            throw new CsobGatewayException(
                errorMessage,
                innerException: exception);
        }

        if (
            normalized.Length > MaximumLength ||
            !string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new CsobGatewayException(errorMessage);
        }

        return value;
    }

    public static string NormalizeBrowserInput(
        string value,
        string parameterName = "payId")
    {
        var normalized = PaymentProviderReference.Normalize(
            value,
            parameterName);

        if (normalized.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"ČSOB payId smí mít nejvýše {MaximumLength} znaků.",
                parameterName);
        }

        return normalized;
    }

    public static string RequireCanonical(
        string value,
        string parameterName = "payId")
    {
        var normalized = NormalizeBrowserInput(value, parameterName);

        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "ČSOB payId musí být v kanonickém tvaru.",
                parameterName);
        }

        return value;
    }

    public static string RequireGatewayInput(string value)
    {
        try
        {
            return RequireCanonical(value, "payId");
        }
        catch (ArgumentException exception)
        {
            throw new CsobGatewayException(
                exception.Message,
                innerException: exception);
        }
    }
}
