using FuaPay.Web.Modules.Receipts.Application;

namespace FuaPay.Web.Tests.Modules.Receipts.Application;

public sealed class ReceiptConfigurationTests
{
    [Fact]
    public void Validate_DisabledConfigurationDoesNotRequireIssuerData()
    {
        var configuration = new ReceiptConfiguration(
            Enabled: false,
            PreviewMode: true,
            Issuer: new ReceiptIssuerConfiguration(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty),
            VatRatePercent: 21,
            LogoPath: "missing.png",
            RegularFontPath: null,
            BoldFontPath: null);

        configuration.Validate("Production");
    }

    [Fact]
    public void Validate_ProductionRejectsPreviewMode()
    {
        using var fixture = ConfigurationFixture.Create(previewMode: true);

        var exception = Assert.Throws<InvalidOperationException>(
            () => fixture.Configuration.Validate("Production"));

        Assert.Contains(
            "preview režimu",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ProductionRejectsMissingFontPaths()
    {
        using var fixture = ConfigurationFixture.Create(
            previewMode: false,
            registrationNumber: "12345678",
            vatNumber: "CZ12345678");

        var exception = Assert.Throws<InvalidOperationException>(
            () => fixture.Configuration.Validate("Production"));

        Assert.Contains(
            "Receipts:RegularFontPath",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Receipts:BoldFontPath",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonPreviewRejectsPlaceholderIdentifiers()
    {
        using var fixture = ConfigurationFixture.Create(previewMode: false);

        var exception = Assert.Throws<InvalidOperationException>(
            () => fixture.Configuration.Validate("Staging"));

        Assert.Contains(
            "zástupné IČO nebo DIČ",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly string _directory;

        private ConfigurationFixture(
            string directory,
            ReceiptConfiguration configuration)
        {
            _directory = directory;
            Configuration = configuration;
        }

        public ReceiptConfiguration Configuration { get; }

        public static ConfigurationFixture Create(
            bool previewMode,
            string registrationNumber = "00000000",
            string vatNumber = "CZ00000000")
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"fuapay-receipts-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var logoPath = Path.Combine(directory, "logo.png");
            File.WriteAllBytes(logoPath, [0x89, 0x50, 0x4E, 0x47]);

            return new ConfigurationFixture(
                directory,
                new ReceiptConfiguration(
                    Enabled: true,
                    PreviewMode: previewMode,
                    Issuer: new ReceiptIssuerConfiguration(
                        "Technická univerzita v Liberci",
                        "Fakulta umění a architektury",
                        "Studentská 1402/2",
                        "461 17 Liberec 1",
                        "Česká republika",
                        registrationNumber,
                        vatNumber,
                        "fua@tul.cz"),
                    VatRatePercent: 21,
                    LogoPath: logoPath,
                    RegularFontPath: null,
                    BoldFontPath: null));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
