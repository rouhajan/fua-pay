using System.Globalization;

using FuaPay.Web.Modules.Receipts.Application;

using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace FuaPay.Web.Modules.Receipts.Infrastructure.Pdf;

internal sealed class PdfSharpReceiptPdfRenderer : IReceiptPdfRenderer
{
    private const double Margin = 42;
    private const double ContentGap = 18;

    private static readonly CultureInfo CzechCulture =
        CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly TimeZoneInfo CzechTimeZone = ResolveCzechTimeZone();
    private static readonly object FontConfigurationGate = new();
    private static string? _configuredFontSignature;

    private readonly ReceiptConfiguration _configuration;

    public PdfSharpReceiptPdfRenderer(
        ReceiptConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public ReceiptPdfFile Render(JobPaymentReceiptData receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (!_configuration.Enabled)
        {
            throw new InvalidOperationException(
                "Generování PDF dokladů není v tomto prostředí povoleno.");
        }

        var fontFamily = ConfigureFonts();

        var document = new PdfDocument();
        document.Info.Title = $"Potvrzení o úhradě {receipt.JobNumber}";
        document.Info.Author = receipt.Issuer.LegalName;
        document.Info.Subject = "Potvrzení o úhradě zakázky FUA Pay";

        var page = document.AddPage();
        page.Size = PageSize.A4;

        using var graphics = XGraphics.FromPdfPage(page);
        using var logo = XImage.FromFile(_configuration.LogoPath);

        var regular = new XFont(fontFamily, 9.5, XFontStyleEx.Regular);
        var small = new XFont(fontFamily, 8, XFontStyleEx.Regular);
        var smallBold = new XFont(fontFamily, 8, XFontStyleEx.Bold);
        var heading = new XFont(fontFamily, 11, XFontStyleEx.Bold);
        var title = new XFont(fontFamily, 20, XFontStyleEx.Bold);
        var total = new XFont(fontFamily, 14, XFontStyleEx.Bold);

        var width = page.Width.Point;
        var contentWidth = width - 2 * Margin;
        var y = Margin;

        var logoWidth = 150d;
        var logoHeight = logoWidth * logo.PixelHeight / logo.PixelWidth;
        graphics.DrawImage(
            logo,
            Margin,
            y,
            logoWidth,
            logoHeight);

        graphics.DrawString(
            "POTVRZENÍ O ÚHRADĚ",
            title,
            XBrushes.Black,
            new XRect(
                Margin + logoWidth + ContentGap,
                y,
                contentWidth - logoWidth - ContentGap,
                28),
            XStringFormats.TopRight);
        graphics.DrawString(
            $"Reference: {receipt.ReceiptReference}",
            regular,
            XBrushes.Black,
            new XRect(
                Margin + logoWidth + ContentGap,
                y + 31,
                contentWidth - logoWidth - ContentGap,
                16),
            XStringFormats.TopRight);

        y += Math.Max(logoHeight, 54) + 16;

        if (receipt.PreviewMode)
        {
            var bannerHeight = 48d;
            graphics.DrawRectangle(
                new XPen(XColors.DarkGray, 0.8),
                XBrushes.LightGray,
                Margin,
                y,
                contentWidth,
                bannerHeight);
            DrawWrappedText(
                graphics,
                "NÁHLED – IČO, DIČ a pravidlo DPH jsou zástupné a nejsou schválené. " +
                "Nejde o finální daňový doklad.",
                smallBold,
                XBrushes.Black,
                Margin + 10,
                y + 9,
                contentWidth - 20,
                13);
            y += bannerHeight + 18;
        }

        var columnGap = 24d;
        var columnWidth = (contentWidth - columnGap) / 2;
        var issuerHeight = DrawIdentityBlock(
            graphics,
            "Vystavitel",
            [
                receipt.Issuer.LegalName,
                receipt.Issuer.UnitName,
                receipt.Issuer.AddressLine1,
                receipt.Issuer.AddressLine2,
                receipt.Issuer.Country,
                $"IČO: {receipt.Issuer.RegistrationNumber}",
                $"DIČ: {receipt.Issuer.VatNumber}",
                receipt.Issuer.ContactEmail
            ],
            Margin,
            y,
            columnWidth,
            heading,
            regular);

        var customerLines = new List<string>
        {
            receipt.CustomerName
        };
        if (!string.IsNullOrWhiteSpace(receipt.CustomerEmail))
        {
            customerLines.Add(receipt.CustomerEmail);
        }

        var customerHeight = DrawIdentityBlock(
            graphics,
            "Zákazník",
            customerLines,
            Margin + columnWidth + columnGap,
            y,
            columnWidth,
            heading,
            regular);

        y += Math.Max(issuerHeight, customerHeight) + 22;
        DrawHorizontalRule(graphics, y, width);
        y += 18;

        graphics.DrawString(
            "Zakázka",
            heading,
            XBrushes.Black,
            new XRect(Margin, y, contentWidth, 18),
            XStringFormats.TopLeft);
        y += 22;

        y = DrawLabelValue(
            graphics,
            "Číslo",
            receipt.JobNumber,
            Margin,
            y,
            contentWidth,
            smallBold,
            regular);
        y = DrawLabelValue(
            graphics,
            "Název",
            receipt.JobTitle,
            Margin,
            y,
            contentWidth,
            smallBold,
            regular);
        y = DrawLabelValue(
            graphics,
            "Pracoviště",
            $"{receipt.ServiceUnitName} ({receipt.ServiceUnitCode})",
            Margin,
            y,
            contentWidth,
            smallBold,
            regular);
        y = DrawLabelValue(
            graphics,
            "Datum úhrady",
            FormatDateTime(receipt.SettledAt),
            Margin,
            y,
            contentWidth,
            smallBold,
            regular);
        y = DrawLabelValue(
            graphics,
            "Způsob úhrady",
            receipt.PaymentProvider is null
                ? receipt.SettlementMethod
                : $"{receipt.SettlementMethod} – {receipt.PaymentProvider}",
            Margin,
            y,
            contentWidth,
            smallBold,
            regular);

        if (!string.IsNullOrWhiteSpace(receipt.ProviderReference))
        {
            y = DrawLabelValue(
                graphics,
                "Reference poskytovatele",
                receipt.ProviderReference,
                Margin,
                y,
                contentWidth,
                smallBold,
                regular);
        }

        y += 8;
        DrawHorizontalRule(graphics, y, width);
        y += 18;

        graphics.DrawString(
            "Částka",
            heading,
            XBrushes.Black,
            new XRect(Margin, y, contentWidth, 18),
            XStringFormats.TopLeft);
        y += 24;

        DrawAmountRow(
            graphics,
            "Základ DPH",
            FormatMoney(receipt.TaxBaseMinorUnits),
            Margin,
            y,
            contentWidth,
            regular);
        y += 19;
        DrawAmountRow(
            graphics,
            $"DPH {receipt.VatRatePercent} %",
            FormatMoney(receipt.VatAmountMinorUnits),
            Margin,
            y,
            contentWidth,
            regular);
        y += 23;
        DrawHorizontalRule(graphics, y, width);
        y += 12;
        DrawAmountRow(
            graphics,
            "Celkem uhrazeno",
            FormatMoney(receipt.GrossAmountMinorUnits),
            Margin,
            y,
            contentWidth,
            total);

        var footerY = page.Height.Point - 78;

        if (!ReceiptTextLayout.FitsBeforeFooter(
                y,
                18,
                ContentGap,
                footerY))
        {
            throw new InvalidOperationException(
                "Obsah PDF potvrzení se nevejde na jednu stránku. " +
                "Dokument nebyl vygenerován.");
        }

        DrawHorizontalRule(graphics, footerY, width);
        graphics.DrawString(
            "Toto potvrzení dokumentuje úhradu evidovanou systémem FUA Pay.",
            small,
            XBrushes.Black,
            new XRect(Margin, footerY + 10, contentWidth, 14),
            XStringFormats.TopLeft);
        graphics.DrawString(
            $"Technická reference: {receipt.SettlementReferenceId:D}",
            small,
            XBrushes.Gray,
            new XRect(Margin, footerY + 28, contentWidth, 14),
            XStringFormats.TopLeft);

        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);

        return new ReceiptPdfFile(
            stream.ToArray(),
            BuildFileName(receipt.JobNumber));
    }

    private string ConfigureFonts()
    {
        var regularPath = _configuration.RegularFontPath;
        var boldPath = _configuration.BoldFontPath;
        var signature = regularPath is not null && boldPath is not null
            ? $"files:{regularPath}|{boldPath}"
            : "windows-platform";

        lock (FontConfigurationGate)
        {
            if (_configuredFontSignature is not null)
            {
                if (!string.Equals(
                        _configuredFontSignature,
                        signature,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "PDFsharp fonty už byly inicializované jinou konfigurací.");
                }

                return regularPath is not null
                    ? ReceiptFontResolver.FamilyName
                    : "Arial";
            }

            if (regularPath is not null && boldPath is not null)
            {
                GlobalFontSettings.FontResolver =
                    new ReceiptFontResolver(regularPath, boldPath);
                _configuredFontSignature = signature;
                return ReceiptFontResolver.FamilyName;
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new InvalidOperationException(
                    "Na Linuxu musí být pro PDF doklady nastavené " +
                    "Receipts:RegularFontPath a Receipts:BoldFontPath.");
            }

            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _configuredFontSignature = signature;
            return "Arial";
        }
    }

    private static double DrawIdentityBlock(
        XGraphics graphics,
        string title,
        IEnumerable<string> lines,
        double x,
        double y,
        double width,
        XFont heading,
        XFont regular)
    {
        graphics.DrawString(
            title,
            heading,
            XBrushes.Black,
            new XRect(x, y, width, 18),
            XStringFormats.TopLeft);

        var currentY = y + 23;
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            currentY = DrawWrappedText(
                graphics,
                line,
                regular,
                XBrushes.Black,
                x,
                currentY,
                width,
                13);
        }

        return currentY - y;
    }

    private static double DrawLabelValue(
        XGraphics graphics,
        string label,
        string value,
        double x,
        double y,
        double width,
        XFont labelFont,
        XFont valueFont)
    {
        const double labelWidth = 112;

        graphics.DrawString(
            label,
            labelFont,
            XBrushes.Black,
            new XRect(x, y, labelWidth, 16),
            XStringFormats.TopLeft);

        var nextY = DrawWrappedText(
            graphics,
            value,
            valueFont,
            XBrushes.Black,
            x + labelWidth,
            y,
            width - labelWidth,
            14);

        return Math.Max(y + 18, nextY + 4);
    }

    private static double DrawWrappedText(
        XGraphics graphics,
        string text,
        XFont font,
        XBrush brush,
        double x,
        double y,
        double width,
        double lineHeight)
    {
        var lines = ReceiptTextLayout.Wrap(
            text,
            width,
            candidate => graphics.MeasureString(candidate, font).Width);
        var currentY = y;

        foreach (var line in lines)
        {
            currentY = DrawTextLine(
                graphics,
                line,
                font,
                brush,
                x,
                currentY,
                width,
                lineHeight);
        }

        return currentY;
    }

    private static double DrawTextLine(
        XGraphics graphics,
        string line,
        XFont font,
        XBrush brush,
        double x,
        double y,
        double width,
        double lineHeight)
    {
        graphics.DrawString(
            line,
            font,
            brush,
            new XRect(x, y, width, lineHeight),
            XStringFormats.TopLeft);

        return y + lineHeight;
    }

    private static void DrawAmountRow(
        XGraphics graphics,
        string label,
        string amount,
        double x,
        double y,
        double width,
        XFont font)
    {
        graphics.DrawString(
            label,
            font,
            XBrushes.Black,
            new XRect(x, y, width * 0.65, 18),
            XStringFormats.TopLeft);
        graphics.DrawString(
            amount,
            font,
            XBrushes.Black,
            new XRect(
                x + width * 0.65,
                y,
                width * 0.35,
                18),
            XStringFormats.TopRight);
    }

    private static void DrawHorizontalRule(
        XGraphics graphics,
        double y,
        double pageWidth)
    {
        graphics.DrawLine(
            new XPen(XColors.Gray, 0.6),
            Margin,
            y,
            pageWidth - Margin,
            y);
    }

    private static string FormatMoney(long minorUnits)
    {
        var crowns = minorUnits / 100m;
        return $"{crowns.ToString("N2", CzechCulture)} Kč";
    }

    private static string FormatDateTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, CzechTimeZone).ToString(
            "d. M. yyyy HH:mm",
            CzechCulture);

    private static TimeZoneInfo ResolveCzechTimeZone()
    {
        foreach (
            var identifier in
            new[] { "Europe/Prague", "Central Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(identifier);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException(
            "Systém neobsahuje časovou zónu Europe/Prague potřebnou pro PDF doklady.");
    }

    private static string BuildFileName(string jobNumber)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safeJobNumber = new string(
            jobNumber
                .Select(character =>
                    invalid.Contains(character) ? '-' : character)
                .ToArray());

        return $"fua-pay-potvrzeni-uhrady-{safeJobNumber}.pdf";
    }
}
