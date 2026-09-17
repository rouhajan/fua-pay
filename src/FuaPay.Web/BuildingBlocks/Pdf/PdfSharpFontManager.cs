using PdfSharp.Fonts;

namespace FuaPay.Web.BuildingBlocks.Pdf;

public sealed class PdfSharpFontManager
{
    private static readonly object Gate = new();
    private static string? _configuredSignature;

    private readonly PdfAssetsConfiguration _configuration;

    public PdfSharpFontManager(PdfAssetsConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public string GetFontFamily()
    {
        var regularPath = _configuration.RegularFontPath;
        var boldPath = _configuration.BoldFontPath;
        var signature = regularPath is not null && boldPath is not null
            ? $"files:{regularPath}|{boldPath}"
            : "windows-platform";

        lock (Gate)
        {
            if (_configuredSignature is not null)
            {
                if (!string.Equals(
                        _configuredSignature,
                        signature,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "PDFsharp fonty už byly inicializované jinou konfigurací.");
                }

                return regularPath is not null
                    ? FuaPayFontResolver.FamilyName
                    : "Arial";
            }

            if (regularPath is not null && boldPath is not null)
            {
                GlobalFontSettings.FontResolver =
                    new FuaPayFontResolver(regularPath, boldPath);
                _configuredSignature = signature;
                return FuaPayFontResolver.FamilyName;
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException(
                    "Na Linuxu musí být pro PDF nastavené cesty k regular a bold fontu.");
            }

            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _configuredSignature = signature;
            return "Arial";
        }
    }
}
