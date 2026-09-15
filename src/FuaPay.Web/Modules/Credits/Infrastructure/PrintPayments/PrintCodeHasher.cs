using System.Security.Cryptography;
using System.Text;

using FuaPay.Web.Modules.Credits.Application;

using Microsoft.AspNetCore.Identity;

namespace FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

internal sealed class PrintCodeHasher : IPrintCodeHasher
{
    private static readonly object HashContext = new();

    private readonly byte[] _pepper;
    private readonly PasswordHasher<object> _passwordHasher = new();

    public PrintCodeHasher(PrintCredentialSecurityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _pepper = configuration.Pepper;
    }

    public string Hash(string printCode)
    {
        EnsureAvailable();
        ValidateCode(printCode);
        return _passwordHasher.HashPassword(HashContext, Prepare(printCode));
    }

    public bool Verify(string hash, string printCode)
    {
        EnsureAvailable();
        ValidateCode(printCode);

        if (string.IsNullOrWhiteSpace(hash) || hash.Length > 512)
        {
            return false;
        }

        try
        {
            return _passwordHasher.VerifyHashedPassword(
                    HashContext,
                    hash,
                    Prepare(printCode)) is not PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private string Prepare(string printCode)
    {
        var codeBytes = Encoding.ASCII.GetBytes(printCode);
        var digest = HMACSHA256.HashData(_pepper, codeBytes);

        try
        {
            return Convert.ToBase64String(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(codeBytes);
            CryptographicOperations.ZeroMemory(digest);
        }
    }

    private void EnsureAvailable()
    {
        if (_pepper.Length < 32)
        {
            throw new InvalidOperationException(
                "Print credential security material is unavailable.");
        }
    }

    private static void ValidateCode(string printCode)
    {
        if (!PrintCodePolicy.IsValid(printCode))
        {
            throw new ArgumentException("Print code must contain exactly six digits.", nameof(printCode));
        }
    }
}
