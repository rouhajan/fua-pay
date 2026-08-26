namespace FuaPay.Web.Modules.Credits.Domain;

public static class IppJobUuid
{
    public const int MaxLength = 45;

    private const string Prefix = "urn:uuid:";

    public static string Normalize(
        string value,
        string parameterName = "jobUuid")
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (
            value.Length != MaxLength ||
            !value.StartsWith(
                Prefix,
                StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(
                value.AsSpan(Prefix.Length),
                "D",
                out var parsed) ||
            parsed == Guid.Empty)
        {
            throw new ArgumentException(
                "The IPP job UUID must be a non-nil URI in urn:uuid:<UUID> format.",
                parameterName);
        }

        return $"{Prefix}{parsed:D}";
    }
}
