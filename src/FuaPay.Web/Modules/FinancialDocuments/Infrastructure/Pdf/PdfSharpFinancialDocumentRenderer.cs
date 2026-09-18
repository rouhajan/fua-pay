using System.Globalization;

using FuaPay.Web.BuildingBlocks.Pdf;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Pdf;

internal sealed class PdfSharpFinancialDocumentRenderer :
    IFinancialDocumentPdfRenderer
{
    private const double Margin = 42;

    private static readonly CultureInfo CzechCulture =
        CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly TimeZoneInfo CzechTimeZone = ResolveCzechTimeZone();

    private readonly PdfAssetsConfiguration _assets;
    private readonly PdfSharpFontManager _fontManager;

    public PdfSharpFinancialDocumentRenderer(
        PdfAssetsConfiguration assets,
        PdfSharpFontManager fontManager)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(fontManager);
        _assets = assets;
        _fontManager = fontManager;
    }

    public FinancialDocumentPdfFile Render(FinancialDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateDispatch(document);

        var content = FinancialDocumentPdfContent.Create(document);
        var issuer = document.Issuer!;
        var fontFamily = _fontManager.GetFontFamily();
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Doklad o úhradě {document.DocumentNumber}";
        pdf.Info.Author = issuer.LegalName;
        pdf.Info.Subject = "Doklad o úhradě FUA Pay";

        var page = pdf.AddPage();
        page.Size = PageSize.A4;
        using var graphics = XGraphics.FromPdfPage(page);
        using var logo = XImage.FromFile(_assets.LogoPath);

        var regular = new XFont(fontFamily, 9.5, XFontStyleEx.Regular);
        var small = new XFont(fontFamily, 8, XFontStyleEx.Regular);
        var label = new XFont(fontFamily, 9.5, XFontStyleEx.Bold);
        var heading = new XFont(fontFamily, 11, XFontStyleEx.Bold);
        var title = new XFont(fontFamily, 20, XFontStyleEx.Bold);
        var total = new XFont(fontFamily, 13, XFontStyleEx.Bold);
        var contentWidth = page.Width.Point - 2 * Margin;
        var y = Margin;

        const double logoWidth = 150;
        var logoHeight = logoWidth * logo.PixelHeight / logo.PixelWidth;
        graphics.DrawImage(logo, Margin, y, logoWidth, logoHeight);
        graphics.DrawString(
            content.Title,
            title,
            XBrushes.Black,
            new XRect(Margin + 170, y, contentWidth - 170, 28),
            XStringFormats.TopRight);
        graphics.DrawString(
            content.DocumentNumber,
            regular,
            XBrushes.Black,
            new XRect(Margin + 170, y + 32, contentWidth - 170, 18),
            XStringFormats.TopRight);

        y += Math.Max(logoHeight, 54) + 18;
        y = DrawIssuer(
            graphics,
            issuer,
            Margin,
            y,
            contentWidth,
            heading,
            regular);
        y += 14;
        DrawRule(graphics, y, page.Width.Point);
        y += 18;

        graphics.DrawString(
            content.Purpose,
            heading,
            XBrushes.Black,
            new XRect(Margin, y, contentWidth, 18),
            XStringFormats.TopLeft);
        y += 24;

        foreach (var detail in content.Details)
        {
            y = DrawLabelValue(
                graphics,
                detail.Label,
                detail.Value,
                y,
                label,
                regular,
                contentWidth);
        }

        y += 6;
        DrawRule(graphics, y, page.Width.Point);
        y += 18;
        DrawAmountRow(graphics, content.Amounts[0].Label, content.Amounts[0].MinorUnits, y, regular, contentWidth);
        y += 20;
        DrawAmountRow(graphics, content.Amounts[1].Label, content.Amounts[1].MinorUnits, y, regular, contentWidth);
        y += 20;
        DrawAmountRow(
            graphics,
            content.Amounts[2].Label,
            content.Amounts[2].MinorUnits,
            y,
            regular,
            contentWidth);
        y += 24;
        DrawRule(graphics, y, page.Width.Point);
        y += 12;
        DrawAmountRow(graphics, content.Amounts[3].Label, content.Amounts[3].MinorUnits, y, total, contentWidth);

        var footerY = page.Height.Point - 64;
        if (!PdfTextLayout.FitsBeforeFooter(y, 20, 18, footerY))
        {
            throw new FinancialDocumentRenderUnavailableException(
                FinancialDocumentRenderUnavailableReason.LayoutOverflow,
                "Obsah finančního dokumentu se nevejde na jednu stránku.");
        }

        DrawRule(graphics, footerY, page.Width.Point);
        graphics.DrawString(
            content.Footer,
            small,
            XBrushes.Gray,
            new XRect(Margin, footerY + 10, contentWidth, 16),
            XStringFormats.TopLeft);

        using var stream = new MemoryStream();
        pdf.Save(stream, closeStream: false);
        return new FinancialDocumentPdfFile(
            stream.ToArray(),
            $"doklad-o-uhrade-{document.DocumentNumber}.pdf");
    }

    private static void ValidateDispatch(FinancialDocument document)
    {
        ValidateVersionDispatch(
            document.SchemaVersion,
            document.RenderVersion);

        if (document.DocumentType is not (
            FinancialDocumentType.ManualCreditTopUp or
            FinancialDocumentType.CardWalletTopUp or
            FinancialDocumentType.DirectJobCardPayment))
        {
            throw new FinancialDocumentRenderUnavailableException(
                FinancialDocumentRenderUnavailableReason.UnsupportedDocumentType,
                $"Document type '{document.DocumentType}' is not renderable.");
        }
    }

    private static double DrawIssuer(
        XGraphics graphics,
        FinancialDocumentIssuerSnapshot issuer,
        double x,
        double y,
        double width,
        XFont heading,
        XFont regular)
    {
        graphics.DrawString("Vystavitel", heading, XBrushes.Black,
            new XRect(x, y, width, 18), XStringFormats.TopLeft);
        var current = y + 23;
        foreach (var value in new[]
        {
            issuer.LegalName,
            issuer.UnitName,
            issuer.AddressLine1,
            issuer.AddressLine2,
            issuer.Country,
            $"IČO: {issuer.RegistrationNumber}",
            $"DIČ: {issuer.VatNumber}",
            issuer.ContactEmail
        })
        {
            current = DrawWrappedText(
                graphics,
                value,
                x,
                current,
                width,
                regular);
        }

        return current;
    }

    private static double DrawLabelValue(
        XGraphics graphics,
        string labelText,
        string value,
        double y,
        XFont label,
        XFont regular,
        double contentWidth)
    {
        const double labelWidth = 120;
        graphics.DrawString(labelText, label, XBrushes.Black,
            new XRect(Margin, y, labelWidth, 18), XStringFormats.TopLeft);
        var valueBottom = DrawWrappedText(
            graphics,
            value,
            Margin + labelWidth,
            y,
            contentWidth - labelWidth,
            regular);
        return Math.Max(y + 20, valueBottom + 7);
    }

    private static double DrawWrappedText(
        XGraphics graphics,
        string text,
        double x,
        double y,
        double width,
        XFont font)
    {
        const double lineHeight = 13;
        var lines = PdfTextLayout.Wrap(
            text,
            width,
            value => graphics.MeasureString(value, font).Width);
        var current = y;

        foreach (var line in lines)
        {
            graphics.DrawString(
                line,
                font,
                XBrushes.Black,
                new XRect(x, current, width, lineHeight),
                XStringFormats.TopLeft);
            current += lineHeight;
        }

        return current;
    }

    internal static void ValidateVersionDispatch(
        int schemaVersion,
        int renderVersion)
    {
        if (schemaVersion == 1 && renderVersion == 1)
        {
            throw new FinancialDocumentRenderUnavailableException(
                FinancialDocumentRenderUnavailableReason.LegacyIncompleteSnapshot,
                "Legacy schema/render 1/1 document has no approved complete snapshot.");
        }

        if (schemaVersion != 2 || renderVersion != 2)
        {
            throw new FinancialDocumentRenderUnavailableException(
                FinancialDocumentRenderUnavailableReason.UnsupportedVersion,
                $"Unsupported financial document schema/render version " +
                $"{schemaVersion}/{renderVersion}.");
        }
    }

    private static void DrawAmountRow(
        XGraphics graphics,
        string label,
        long amountMinorUnits,
        double y,
        XFont font,
        double contentWidth)
    {
        graphics.DrawString(label, font, XBrushes.Black,
            new XRect(Margin, y, contentWidth * 0.65, 18), XStringFormats.TopLeft);
        graphics.DrawString(FormatMoney(amountMinorUnits), font, XBrushes.Black,
            new XRect(Margin + contentWidth * 0.65, y, contentWidth * 0.35, 18),
            XStringFormats.TopRight);
    }

    private static void DrawRule(XGraphics graphics, double y, double pageWidth) =>
        graphics.DrawLine(new XPen(XColors.Gray, 0.6), Margin, y, pageWidth - Margin, y);

    private static string FormatMoney(long minorUnits) =>
        $"{(minorUnits / 100m).ToString("N2", CzechCulture)} Kč";

    internal static string FormatDateTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, CzechTimeZone).ToString(
            "d. M. yyyy HH:mm",
            CzechCulture);

    private static TimeZoneInfo ResolveCzechTimeZone()
    {
        foreach (var identifier in new[] { "Europe/Prague", "Central Europe Standard Time" })
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
            "Systém neobsahuje časovou zónu Europe/Prague potřebnou pro PDF.");
    }
}

internal sealed record FinancialDocumentPdfContent(
    string Title,
    string DocumentNumber,
    string Purpose,
    IReadOnlyList<FinancialDocumentPdfDetail> Details,
    IReadOnlyList<FinancialDocumentPdfAmount> Amounts,
    string Footer)
{
    internal static FinancialDocumentPdfContent Create(
        FinancialDocument document)
    {
        var details = new List<FinancialDocumentPdfDetail>();
        if (document.DocumentType == FinancialDocumentType.DirectJobCardPayment)
        {
            var job = document.Job!;
            details.Add(new("Název zakázky", job.Title));
            details.Add(new("Pracoviště", job.ServiceUnitName));
            details.Add(new("Číslo zakázky", job.JobNumber));
        }

        details.Add(new(
            "Datum úhrady",
            PdfSharpFinancialDocumentRenderer.FormatDateTime(
                document.FinancialEventAt)));
        details.Add(new(
            "Způsob úhrady",
            document.SettlementMethod ==
                FinancialDocumentSettlementMethod.ManualCreditTopUp
                    ? "Ruční dobití kreditu"
                    : "Platební karta"));

        var tax = document.Tax!;
        return new FinancialDocumentPdfContent(
            "Doklad o úhradě",
            document.DocumentNumber,
            document.DocumentType is (
                FinancialDocumentType.ManualCreditTopUp or
                FinancialDocumentType.CardWalletTopUp)
                ? "Dobití kreditu"
                : "Úhrada zakázky",
            details,
            [
                new("Částka včetně DPH", document.AmountMinorUnits),
                new("Základ bez DPH", tax.TaxBaseMinorUnits),
                new(
                    $"DPH {tax.VatRateBasisPoints / 100m:0.##} %",
                    tax.VatAmountMinorUnits),
                new("Celkem uhrazeno", document.AmountMinorUnits)
            ],
            "Doklad byl vystaven na základě úhrady evidované systémem FUA Pay.");
    }
}

internal sealed record FinancialDocumentPdfDetail(string Label, string Value);

internal sealed record FinancialDocumentPdfAmount(string Label, long MinorUnits);
