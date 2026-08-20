namespace FuaPay.Web.Modules.Receipts.Application;

public sealed record ReceiptIssuerConfiguration(
    string LegalName,
    string UnitName,
    string AddressLine1,
    string AddressLine2,
    string Country,
    string RegistrationNumber,
    string VatNumber,
    string ContactEmail);

public sealed record ReceiptConfiguration(
    bool Enabled,
    bool PreviewMode,
    ReceiptIssuerConfiguration Issuer,
    int VatRatePercent,
    string LogoPath,
    string? RegularFontPath,
    string? BoldFontPath)
{
    private const string PreviewRegistrationNumber = "00000000";
    private const string PreviewVatNumber = "CZ00000000";

    public static ReceiptConfiguration Resolve(
        IConfiguration configuration,
        string environmentName,
        string webRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(webRootPath);

        var result = new ReceiptConfiguration(
            configuration.GetValue<bool>("Receipts:Enabled"),
            configuration.GetValue<bool>("Receipts:PreviewMode"),
            new ReceiptIssuerConfiguration(
                configuration["Receipts:Issuer:LegalName"]?.Trim() ?? string.Empty,
                configuration["Receipts:Issuer:UnitName"]?.Trim() ?? string.Empty,
                configuration["Receipts:Issuer:AddressLine1"]?.Trim() ?? string.Empty,
                configuration["Receipts:Issuer:AddressLine2"]?.Trim() ?? string.Empty,
                configuration["Receipts:Issuer:Country"]?.Trim() ?? string.Empty,
                configuration["Receipts:Issuer:RegistrationNumber"]?.Trim() ?? string.Empty,
                configuration["Receipts:Issuer:VatNumber"]?.Trim() ?? string.Empty,
                configuration["Receipts:Issuer:ContactEmail"]?.Trim() ?? string.Empty),
            configuration.GetValue<int?>("Receipts:VatRatePercent") ?? 21,
            Path.Combine(webRootPath, "images", "fua-tul-logo.png"),
            NormalizeOptionalPath(configuration["Receipts:RegularFontPath"]),
            NormalizeOptionalPath(configuration["Receipts:BoldFontPath"]));

        result.Validate(environmentName);
        return result;
    }

    public void Validate(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        if (!Enabled)
        {
            return;
        }

        ValidateRequired(Issuer.LegalName, "Receipts:Issuer:LegalName");
        ValidateRequired(Issuer.UnitName, "Receipts:Issuer:UnitName");
        ValidateRequired(Issuer.AddressLine1, "Receipts:Issuer:AddressLine1");
        ValidateRequired(Issuer.AddressLine2, "Receipts:Issuer:AddressLine2");
        ValidateRequired(Issuer.Country, "Receipts:Issuer:Country");
        ValidateRequired(
            Issuer.RegistrationNumber,
            "Receipts:Issuer:RegistrationNumber");
        ValidateRequired(Issuer.VatNumber, "Receipts:Issuer:VatNumber");
        ValidateRequired(Issuer.ContactEmail, "Receipts:Issuer:ContactEmail");

        if (VatRatePercent is < 0 or > 100)
        {
            throw new InvalidOperationException(
                "Receipts:VatRatePercent musí být mezi 0 a 100.");
        }

        if (!File.Exists(LogoPath))
        {
            throw new InvalidOperationException(
                $"Logo pro doklad nebylo nalezeno: {LogoPath}");
        }

        if ((RegularFontPath is null) != (BoldFontPath is null))
        {
            throw new InvalidOperationException(
                "Receipts:RegularFontPath a Receipts:BoldFontPath musí být " +
                "nastavené buď oba, nebo ani jeden.");
        }

        ValidateOptionalFontPath(RegularFontPath, "Receipts:RegularFontPath");
        ValidateOptionalFontPath(BoldFontPath, "Receipts:BoldFontPath");

        var isProduction = string.Equals(
            environmentName,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase);

        if (isProduction && PreviewMode)
        {
            throw new InvalidOperationException(
                "Produkční doklady nesmí běžet v preview režimu.");
        }

        if (
            isProduction &&
            (RegularFontPath is null || BoldFontPath is null))
        {
            throw new InvalidOperationException(
                "V Production musí být při zapnutých PDF dokladech " +
                "nastavené Receipts:RegularFontPath a Receipts:BoldFontPath.");
        }

        if (
            !PreviewMode &&
            (
                string.Equals(
                    Issuer.RegistrationNumber,
                    PreviewRegistrationNumber,
                    StringComparison.Ordinal) ||
                string.Equals(
                    Issuer.VatNumber,
                    PreviewVatNumber,
                    StringComparison.OrdinalIgnoreCase)
            ))
        {
            throw new InvalidOperationException(
                "Mimo preview režim nelze použít zástupné IČO nebo DIČ.");
        }
    }

    private static string? NormalizeOptionalPath(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static void ValidateRequired(
        string value,
        string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{configurationKey} musí být při zapnutých dokladech vyplněno.");
        }
    }

    private static void ValidateOptionalFontPath(
        string? path,
        string configurationKey)
    {
        if (path is null)
        {
            return;
        }

        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{configurationKey} musí ukazovat na existující absolutní soubor fontu.");
        }
    }
}
