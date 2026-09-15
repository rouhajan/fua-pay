using System.Net.Mail;
using System.Text;

namespace FuaPay.Web.Modules.Credits.Domain;

public static class PrintCredentialEmail
{
    public const int MaximumLength = 320;

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var characters = value
            .Normalize(NormalizationForm.FormKC)
            .Trim(' ')
            .ToCharArray();

        for (var index = 0; index < characters.Length; index++)
        {
            if (characters[index] is >= 'A' and <= 'Z')
            {
                characters[index] = (char)(characters[index] + ('a' - 'A'));
            }
        }

        var normalized = new string(characters);

        if (
            normalized.Length > MaximumLength ||
            !MailAddress.TryCreate(normalized, out var parsed) ||
            !string.Equals(parsed.Address, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException("Printing e-mail is invalid.", nameof(value));
        }

        return normalized;
    }

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;

        try
        {
            if (value is null)
            {
                return false;
            }

            normalized = Normalize(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
