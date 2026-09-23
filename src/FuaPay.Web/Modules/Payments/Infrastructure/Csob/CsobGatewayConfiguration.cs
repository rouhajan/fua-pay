namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed record CsobGatewayConfiguration(
    bool Enabled,
    Uri ApiBaseUri,
    string MerchantId,
    string PrivateKeyPath,
    string GatewayPublicKeyPath,
    Uri ReturnUri,
    int PaymentTtlSeconds,
    TimeSpan RequestTimeout)
{
    public static readonly Uri IntegrationApiBaseUri =
        new("https://iapi.iplatebnibrana.csob.cz/");

    public static readonly Uri IntegrationPaymentPageBaseUri =
        new("https://iplatebnibrana.csob.cz/");

    public static readonly Uri SandboxApiBaseUri =
        IntegrationApiBaseUri;

    public static readonly Uri ProductionApiBaseUri =
        new("https://api.platebnibrana.csob.cz/");

    public static readonly Uri ProductionPaymentPageBaseUri =
        new("https://platebnibrana.csob.cz/");

    public static readonly Uri ProductionReturnUri =
        new("https://fuapay.tul.cz/payments/csob/return");

    public static readonly Uri StagingReturnUri =
        new("https://fuapay.fa.tul.cz:8443/payments/csob/return");

    public IReadOnlyList<Uri> BrowserFormActionOrigins =>
        ApiBaseUri == IntegrationApiBaseUri
            ? [IntegrationApiBaseUri, IntegrationPaymentPageBaseUri]
            : ApiBaseUri == ProductionApiBaseUri
                ? [ProductionApiBaseUri, ProductionPaymentPageBaseUri]
                : throw new InvalidOperationException(
                    "ČSOB browser form-action hranice není definována " +
                    "pro nakonfigurované API prostředí.");

    public static CsobGatewayConfiguration Resolve(
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration.GetValue<bool>("Csob:Enabled");
        var isProduction = string.Equals(
            environmentName,
            "Production",
            StringComparison.OrdinalIgnoreCase);
        var apiBaseUri = ParseUri(
            configuration["Csob:ApiBaseUrl"],
            isProduction
                ? ProductionApiBaseUri
                : IntegrationApiBaseUri,
            "Csob:ApiBaseUrl");
        var merchantId =
            configuration["Csob:MerchantId"]?.Trim() ?? string.Empty;
        var privateKeyPath =
            configuration["Csob:PrivateKeyPath"]?.Trim() ?? string.Empty;
        var gatewayPublicKeyPath =
            configuration["Csob:GatewayPublicKeyPath"]?.Trim() ?? string.Empty;
        var returnUri = ParseUri(
            configuration["Csob:ReturnUrl"],
            new Uri("https://localhost/payments/csob/return"),
            "Csob:ReturnUrl");
        var ttlSeconds = configuration.GetValue<int?>(
                "Csob:PaymentTtlSeconds")
            ?? 900;
        var timeoutSeconds = configuration.GetValue<int?>(
                "Csob:RequestTimeoutSeconds")
            ?? 30;

        var resolved = new CsobGatewayConfiguration(
            enabled,
            apiBaseUri,
            merchantId,
            privateKeyPath,
            gatewayPublicKeyPath,
            returnUri,
            ttlSeconds,
            TimeSpan.FromSeconds(timeoutSeconds));

        resolved.Validate(environmentName);
        return resolved;
    }

    public void Validate(string environmentName)
    {
        if (!Enabled)
        {
            return;
        }

        var isDevelopment = string.Equals(
            environmentName,
            "Development",
            StringComparison.OrdinalIgnoreCase);
        var isStaging = string.Equals(
            environmentName,
            "Staging",
            StringComparison.OrdinalIgnoreCase);
        var isProduction = string.Equals(
            environmentName,
            "Production",
            StringComparison.OrdinalIgnoreCase);

        if (
            !isDevelopment &&
            !isStaging &&
            !isProduction)
        {
            throw new InvalidOperationException(
                "Enabled CSOB configuration is allowed only in " +
                "Development, Staging, or Production environment.");
        }

        if (
            isProduction &&
            ApiBaseUri != ProductionApiBaseUri)
        {
            throw new InvalidOperationException(
                "Produkční ČSOB konfigurace smí používat pouze " +
                "oficiální produkční API adresu.");
        }

        if (
            !isProduction &&
            ApiBaseUri != IntegrationApiBaseUri)
        {
            throw new InvalidOperationException(
                "Neprodukční ČSOB konfigurace smí používat pouze " +
                "oficiální integrační API adresu.");
        }

        if (
            string.IsNullOrWhiteSpace(MerchantId) ||
            MerchantId.Length > 10 ||
            MerchantId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)))
        {
            throw new InvalidOperationException(
                "Csob:MerchantId musí mít 1 až 10 ASCII písmen nebo číslic.");
        }

        ValidateKeyPath(PrivateKeyPath, "Csob:PrivateKeyPath");
        ValidateKeyPath(
            GatewayPublicKeyPath,
            "Csob:GatewayPublicKeyPath");

        if (ReturnUri.AbsoluteUri.Length > 300)
        {
            throw new InvalidOperationException(
                "Csob:ReturnUrl smí mít nejvýše 300 znaků.");
        }

        if (
            ReturnUri.Scheme != Uri.UriSchemeHttps ||
            ReturnUri.IsLoopback &&
            !string.Equals(
                environmentName,
                "Development",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Csob:ReturnUrl musí být veřejná HTTPS adresa; loopback je povolen pouze ve Development prostředí.");
        }

        if (
            isProduction &&
            !HasExactReturnBoundary(
                ReturnUri,
                ProductionReturnUri))
        {
            throw new InvalidOperationException(
                "Production CSOB configuration must use only " +
                $"return URL {ProductionReturnUri.AbsoluteUri}.");
        }

        if (
            isStaging &&
            !HasExactReturnBoundary(
                ReturnUri,
                StagingReturnUri))
        {
            throw new InvalidOperationException(
                "Staging CSOB configuration must use only " +
                $"return URL {StagingReturnUri.AbsoluteUri}.");
        }

        if (PaymentTtlSeconds is < 300 or > 1800)
        {
            throw new InvalidOperationException(
                "Csob:PaymentTtlSeconds musí být v rozsahu 300 až 1800 sekund.");
        }

        if (
            RequestTimeout < TimeSpan.FromSeconds(5) ||
            RequestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException(
                "Csob:RequestTimeoutSeconds musí být v rozsahu 5 až 120 sekund.");
        }
    }

    private static bool HasExactReturnBoundary(
        Uri actual,
        Uri expected) =>
        string.Equals(
            actual.Scheme,
            expected.Scheme,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            actual.Host,
            expected.Host,
            StringComparison.OrdinalIgnoreCase) &&
        actual.Port == expected.Port &&
        string.Equals(
            actual.AbsolutePath,
            expected.AbsolutePath,
            StringComparison.Ordinal) &&
        string.IsNullOrEmpty(actual.UserInfo) &&
        string.IsNullOrEmpty(actual.Query) &&
        string.IsNullOrEmpty(actual.Fragment);

    private static Uri ParseUri(
        string? value,
        Uri defaultValue,
        string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{configurationKey} musí být platná absolutní HTTPS adresa.");
        }

        return uri;
    }

    private static void ValidateKeyPath(
        string path,
        string configurationKey)
    {
        if (
            string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{configurationKey} musí ukazovat na existující soubor mimo repozitář.");
        }
    }
}
