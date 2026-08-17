using System.Buffers;
using System.Globalization;
using System.Text;

namespace FuaPay.Web.Modules.Access.Domain;

public sealed record ExternalIdentityKey
{
    public ExternalIdentityKey(
        string provider,
        string tenant,
        string subject)
    {
        Provider = NormalizeProvider(provider);

        Tenant = NormalizeOpaqueIdentifier(
            tenant,
            AccessTextLimits.ExternalTenantMaxLength,
            nameof(tenant));

        Subject = NormalizeOpaqueIdentifier(
            subject,
            AccessTextLimits.ExternalSubjectMaxLength,
            nameof(subject));
    }

    public static ExternalIdentityKey FromGuidIdentifiers(
        string provider,
        string tenant,
        string subject)
    {
        var tenantId = ParseRequiredGuid(
            tenant,
            nameof(tenant));

        var subjectId = ParseRequiredGuid(
            subject,
            nameof(subject));

        return new ExternalIdentityKey(
            provider,
            tenantId.ToString("D"),
            subjectId.ToString("D"));
    }

    public string Provider { get; }

    public string Tenant { get; }

    public string Subject { get; }

    private static Guid ParseRequiredGuid(
        string value,
        string parameterName)
    {
        if (
            !Guid.TryParse(value, out var parsed) ||
            parsed == Guid.Empty)
        {
            throw new ArgumentException(
                "Identifikátor externí identity musí být neprázdné GUID.",
                parameterName);
        }

        return parsed;
    }

    private static string NormalizeProvider(string provider)
    {
        var normalized = NormalizeRequired(
                provider,
                AccessTextLimits.ExternalProviderMaxLength,
                nameof(provider))
            .ToLowerInvariant();

        if (!normalized.All(IsProviderCharacter))
        {
            throw new ArgumentException(
                "Poskytovatel externí identity smí obsahovat pouze " +
                "ASCII písmena, číslice, tečku, pomlčku a podtržítko.",
                nameof(provider));
        }

        return normalized;
    }

    private static bool IsProviderCharacter(char character)
    {
        return
            character is >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '.' or '-' or '_';
    }

    private static string NormalizeOpaqueIdentifier(
        string value,
        int maxLength,
        string parameterName)
    {
        return NormalizeRequired(
            value,
            maxLength,
            parameterName);
    }

    private static string NormalizeRequired(
        string value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Hodnota externí identity nesmí být prázdná.",
                parameterName);
        }

        if (ContainsForbiddenCharacter(value))
        {
            throw new ArgumentException(
                "Hodnota externí identity obsahuje zakázaný znak.",
                parameterName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"Hodnota externí identity smí mít nejvýše " +
                $"{maxLength} znaků.",
                parameterName);
        }

        return normalized;
    }

    private static bool ContainsForbiddenCharacter(string value)
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
                return true;
            }

            var category = Rune.GetUnicodeCategory(rune);

            if (category is
                UnicodeCategory.Control or
                UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator or
                UnicodeCategory.PrivateUse or
                UnicodeCategory.OtherNotAssigned)
            {
                return true;
            }

            remaining = remaining[consumed..];
        }

        return false;
    }
}
