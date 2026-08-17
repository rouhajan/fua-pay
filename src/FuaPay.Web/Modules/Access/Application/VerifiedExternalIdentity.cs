using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed record VerifiedExternalIdentity
{
    public VerifiedExternalIdentity(
        ExternalIdentityKey key,
        string displayName,
        string? email)
    {
        ArgumentNullException.ThrowIfNull(key);

        Key = key;
        DisplayName = NormalizeDisplayName(displayName);
        Email = NormalizeEmail(email);
    }

    public ExternalIdentityKey Key { get; }

    public string DisplayName { get; }

    public string? Email { get; }

    private static string NormalizeDisplayName(
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Zobrazované jméno ověřené identity nesmí být prázdné.",
                nameof(displayName));
        }

        var normalized = displayName.Trim();

        if (
            normalized.Length >
            AccessTextLimits.DisplayNameMaxLength)
        {
            throw new ArgumentException(
                "Zobrazované jméno ověřené identity je příliš dlouhé.",
                nameof(displayName));
        }

        return normalized;
    }

    private static string? NormalizeEmail(string? email)
    {
        if (email is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "E-mail ověřené identity nesmí být prázdný řetězec.",
                nameof(email));
        }

        var normalized = email.Trim();

        if (
            normalized.Length >
            AccessTextLimits.EmailMaxLength)
        {
            throw new ArgumentException(
                "E-mail ověřené identity je příliš dlouhý.",
                nameof(email));
        }

        return normalized;
    }
}
