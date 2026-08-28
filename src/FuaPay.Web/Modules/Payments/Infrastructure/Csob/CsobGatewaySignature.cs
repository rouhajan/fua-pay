using System.Security.Cryptography;
using System.Text;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed class CsobGatewaySignature :
    ICsobGatewaySignature,
    IDisposable
{
    private readonly RSA _merchantPrivateKey;
    private readonly RSA _gatewayPublicKey;

    public CsobGatewaySignature(CsobGatewayConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var merchantPrivateKey = ImportKey(
            configuration.PrivateKeyPath,
            requirePrivateKey: true);

        try
        {
            _gatewayPublicKey = ImportKey(
                configuration.GatewayPublicKeyPath,
                requirePrivateKey: false);
            _merchantPrivateKey = merchantPrivateKey;
        }
        catch
        {
            merchantPrivateKey.Dispose();
            throw;
        }
    }

    public string Sign(string textToSign)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textToSign);

        var signature = _merchantPrivateKey.SignData(
            Encoding.UTF8.GetBytes(textToSign),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(signature);
    }

    public bool Verify(string textToSign, string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textToSign);

        if (
            string.IsNullOrWhiteSpace(signature) ||
            signature.Any(char.IsWhiteSpace))
        {
            return false;
        }

        try
        {
            return _gatewayPublicKey.VerifyData(
                Encoding.UTF8.GetBytes(textToSign),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _merchantPrivateKey.Dispose();
        _gatewayPublicKey.Dispose();
    }

    private static RSA ImportKey(
        string path,
        bool requirePrivateKey)
    {
        var key = RSA.Create();

        try
        {
            var pem = File.ReadAllText(path, Encoding.UTF8);
            key.ImportFromPem(pem);

            if (key.KeySize < 2048)
            {
                throw new CryptographicException(
                    "RSA klíč ČSOB musí mít nejméně 2048 bitů.");
            }

            if (requirePrivateKey)
            {
                _ = key.ExportParameters(includePrivateParameters: true);
            }
            else
            {
                _ = key.ExportParameters(includePrivateParameters: false);
            }

            return key;
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                ArgumentException or
                CryptographicException)
        {
            key.Dispose();
            throw new InvalidOperationException(
                requirePrivateKey
                    ? "Privátní klíč ČSOB nelze načíst jako platný RSA PEM klíč."
                    : "Veřejný klíč brány ČSOB nelze načíst jako platný RSA PEM klíč.");
        }
    }
}
