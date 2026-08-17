using System.Security.Cryptography;

using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobGatewaySignatureTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"fua-pay-csob-signature-{Guid.NewGuid():N}");

    [Fact]
    public void SignAndVerify_UsesRsaSha256Pkcs1()
    {
        Directory.CreateDirectory(_directory);
        var merchantPrivateKeyPath = Path.Combine(
            _directory,
            "merchant-private.pem");
        var gatewayPublicKeyPath = Path.Combine(
            _directory,
            "gateway-public.pem");

        using var merchant = RSA.Create(2048);
        using var gateway = RSA.Create(2048);

        File.WriteAllText(
            merchantPrivateKeyPath,
            merchant.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(
            gatewayPublicKeyPath,
            gateway.ExportSubjectPublicKeyInfoPem());

        var configuration = new CsobGatewayConfiguration(
            Enabled: true,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            "M1MIPS0000",
            merchantPrivateKeyPath,
            gatewayPublicKeyPath,
            new Uri("https://shop.example.com/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));

        using var signature = new CsobGatewaySignature(configuration);
        var text = "M1MIPS0000|5547|20220125131559";
        var signed = signature.Sign(text);

        using var merchantPublic = RSA.Create();
        merchantPublic.ImportFromPem(
            merchant.ExportSubjectPublicKeyInfoPem());

        Assert.True(merchantPublic.VerifyData(
            System.Text.Encoding.UTF8.GetBytes(text),
            Convert.FromBase64String(signed),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        var gatewaySignature = Convert.ToBase64String(
            gateway.SignData(
                System.Text.Encoding.UTF8.GetBytes(text),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

        Assert.True(signature.Verify(text, gatewaySignature));
        Assert.False(signature.Verify(text + "x", gatewaySignature));
        Assert.False(signature.Verify(text, "not-base64"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
