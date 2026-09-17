using FuaPay.Web.BuildingBlocks.Pdf;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Pdf;

using PdfSharp.Pdf.IO;

namespace FuaPay.Web.Tests.Modules.FinancialDocuments.Application;

public sealed class FinancialDocumentPdfRendererTests
{
    [Fact]
    public void Render_ManualTopUpProducesOnePagePdfAndSafeName()
    {
        var document = CreateDocument(FinancialDocumentType.ManualCreditTopUp);
        var renderer = CreateRenderer();

        var result = renderer.Render(document);

        var qaOutput = Environment.GetEnvironmentVariable(
            "FUA_PAY_PDF_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaOutput))
        {
            File.WriteAllBytes(qaOutput, result.Content);
        }

        Assert.Equal(
            $"doklad-o-uhrade-{document.DocumentNumber}.pdf",
            result.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(result.Content, 0, 4));
        using var stream = new MemoryStream(result.Content);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Single(pdf.Pages);
        Assert.Equal(
            $"Doklad o úhradě {document.DocumentNumber}",
            pdf.Info.Title);
        Assert.Equal("Technická univerzita v Liberci", pdf.Info.Author);
    }

    [Fact]
    public void Render_LegacyAndUnknownVersionsFailWithTypedReason()
    {
        var renderer = CreateRenderer();
        var legacy = CreateLegacyDocument();

        var legacyError = Assert.Throws<FinancialDocumentRenderUnavailableException>(
            () => renderer.Render(legacy));
        var futureError = Assert.Throws<FinancialDocumentRenderUnavailableException>(
            () => PdfSharpFinancialDocumentRenderer.ValidateVersionDispatch(
                3,
                3));

        Assert.Equal(
            FinancialDocumentRenderUnavailableReason.LegacyIncompleteSnapshot,
            legacyError.Reason);
        Assert.Equal(
            FinancialDocumentRenderUnavailableReason.UnsupportedVersion,
            futureError.Reason);
    }

    [Fact]
    public void Render_CardWalletTypeIsExplicitlyUnsupportedInStageC()
    {
        var document = new FinancialDocument(
            Guid.NewGuid(),
            "FUA-2026-000001",
            FinancialDocumentType.CardWalletTopUp,
            FinancialDocumentSourceType.Payment,
            Guid.NewGuid(),
            Customer(),
            12_100,
            "CZK",
            IssuedAt,
            IssuedAt,
            FinancialDocumentSettlementMethod.PaymentProvider,
            Issuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(12_100),
            new FinancialDocumentProviderSnapshot("ČSOB", "SECRET-PAY-ID", "SECRET-VS"),
            null,
            2,
            2);

        var error = Assert.Throws<FinancialDocumentRenderUnavailableException>(
            () => CreateRenderer().Render(document));

        Assert.Equal(
            FinancialDocumentRenderUnavailableReason.UnsupportedDocumentType,
            error.Reason);
    }

    [Theory]
    [InlineData(FinancialDocumentType.ManualCreditTopUp)]
    [InlineData(FinancialDocumentType.DirectJobCardPayment)]
    public void Content_UsesOnlyApprovedVisibleSnapshotFields(
        FinancialDocumentType type)
    {
        var document = CreateDocument(type);
        var content = FinancialDocumentPdfContent.Create(document);
        var visible = string.Join(
            "|",
            new[]
            {
                content.Title,
                content.DocumentNumber,
                content.Purpose,
                content.Footer
            }
            .Concat(content.Details.SelectMany(item => new[] { item.Label, item.Value }))
            .Concat(content.Amounts.Select(item => item.Label)));

        Assert.Contains("Doklad o úhradě", visible);
        Assert.Contains(document.DocumentNumber, visible);
        Assert.Contains("Částka včetně DPH", visible);
        Assert.Contains("Základ bez DPH", visible);
        Assert.Contains("DPH 21 %", visible);
        Assert.Contains("Celkem uhrazeno", visible);
        Assert.DoesNotContain(document.Customer.DisplayName, visible);
        Assert.DoesNotContain(document.Customer.Email!, visible);
        Assert.DoesNotContain(document.SourceId.ToString(), visible);
        Assert.DoesNotContain("SECRET-PAY-ID", visible);
        Assert.DoesNotContain("SECRET-VS", visible);
        Assert.DoesNotContain("SECRET-DESCRIPTION", visible);

        if (type == FinancialDocumentType.DirectJobCardPayment)
        {
            Assert.Contains("Model fakulty", visible);
            Assert.Contains("Ateliér", visible);
            Assert.Contains("ZAK-2026-001", visible);
        }
        else
        {
            Assert.Contains("Dobití kreditu", visible);
        }
    }

    [Fact]
    public void Content_UsesPersistedTaxSnapshotValues()
    {
        var document = new FinancialDocument(
            Guid.NewGuid(),
            "FUA-2026-000003",
            FinancialDocumentType.ManualCreditTopUp,
            FinancialDocumentSourceType.ManualCreditTopUp,
            Guid.NewGuid(),
            Customer(),
            12_100,
            "CZK",
            IssuedAt,
            IssuedAt,
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            Issuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(12_100),
            null,
            null,
            2,
            2);

        var content = FinancialDocumentPdfContent.Create(document);

        Assert.Equal(document.Tax!.TaxBaseMinorUnits, content.Amounts[1].MinorUnits);
        Assert.Equal(document.Tax.VatAmountMinorUnits, content.Amounts[2].MinorUnits);
    }

    [Fact]
    public void Render_MaximumJobTitleAndServiceUnitLengthsWrapOnOnePage()
    {
        var document = CreateDirectJobDocument(
            Issuer(),
            new string('Ž', 200),
            new string('Č', 128));

        var result = CreateRenderer().Render(document);

        var qaOutput = Environment.GetEnvironmentVariable(
            "FUA_PAY_PDF_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaOutput))
        {
            File.WriteAllBytes(qaOutput, result.Content);
        }

        using var stream = new MemoryStream(result.Content);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Single(pdf.Pages);
    }

    [Fact]
    public void Render_ContentThatCannotFitFailsClosedWithTypedReason()
    {
        var longValue = new string('Ž', 1_000);
        var issuer = new FinancialDocumentIssuerSnapshot(
            longValue,
            longValue,
            longValue,
            longValue,
            longValue,
            longValue,
            longValue,
            longValue);
        var document = CreateDirectJobDocument(
            issuer,
            new string('Ž', 200),
            new string('Č', 128));

        var error = Assert.Throws<FinancialDocumentRenderUnavailableException>(
            () => CreateRenderer().Render(document));

        Assert.Equal(
            FinancialDocumentRenderUnavailableReason.LayoutOverflow,
            error.Reason);
    }

    private static FinancialDocument CreateDirectJobDocument(
        FinancialDocumentIssuerSnapshot issuer,
        string title,
        string serviceUnitName) =>
        new(
            Guid.NewGuid(),
            "FUA-2026-000004",
            FinancialDocumentType.DirectJobCardPayment,
            FinancialDocumentSourceType.Payment,
            Guid.NewGuid(),
            Customer(),
            12_100,
            "CZK",
            IssuedAt,
            IssuedAt,
            FinancialDocumentSettlementMethod.PaymentProvider,
            issuer,
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(12_100),
            new FinancialDocumentProviderSnapshot(
                "ČSOB",
                "SECRET-PAY-ID",
                "SECRET-VS"),
            new FinancialDocumentJobSnapshot(
                Guid.NewGuid(),
                "ZAK-2026-001",
                title,
                "SECRET-DESCRIPTION",
                serviceUnitName),
            2,
            2);

    internal static FinancialDocument CreateDocument(
        FinancialDocumentType type)
    {
        var direct = type == FinancialDocumentType.DirectJobCardPayment;
        return new FinancialDocument(
            Guid.NewGuid(),
            "FUA-2026-000001",
            type,
            direct
                ? FinancialDocumentSourceType.Payment
                : FinancialDocumentSourceType.ManualCreditTopUp,
            Guid.NewGuid(),
            Customer(),
            12_100,
            "CZK",
            IssuedAt,
            IssuedAt,
            direct
                ? FinancialDocumentSettlementMethod.PaymentProvider
                : FinancialDocumentSettlementMethod.ManualCreditTopUp,
            Issuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(12_100),
            direct
                ? new FinancialDocumentProviderSnapshot(
                    "ČSOB",
                    "SECRET-PAY-ID",
                    "SECRET-VS")
                : null,
            direct
                ? new FinancialDocumentJobSnapshot(
                    Guid.NewGuid(),
                    "ZAK-2026-001",
                    "Model fakulty",
                    "SECRET-DESCRIPTION",
                    "Ateliér")
                : null,
            2,
            2);
    }

    private static FinancialDocument CreateLegacyDocument() =>
        new(
            Guid.NewGuid(),
            "FUA-2026-000002",
            FinancialDocumentType.ManualCreditTopUp,
            FinancialDocumentSourceType.ManualCreditTopUp,
            Guid.NewGuid(),
            Customer(),
            100,
            "CZK",
            IssuedAt,
            IssuedAt,
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            null,
            null,
            null,
            null,
            1,
            1);

    private static PdfSharpFinancialDocumentRenderer CreateRenderer()
    {
        var root = FindRepositoryRoot();
        var regularFontPath = OperatingSystem.IsWindows()
            ? null
            : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        var boldFontPath = OperatingSystem.IsWindows()
            ? null
            : "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf";
        var assets = new PdfAssetsConfiguration(
            Path.Combine(root, "src", "FuaPay.Web", "wwwroot", "images", "fua-tul-logo.png"),
            regularFontPath,
            boldFontPath);
        return new PdfSharpFinancialDocumentRenderer(
            assets,
            new PdfSharpFontManager(assets));
    }

    private static FinancialDocumentCustomerSnapshot Customer() =>
        new(Guid.NewGuid(), "SECRET CUSTOMER", "secret@example.test");

    private static FinancialDocumentIssuerSnapshot Issuer() =>
        new(
            "Technická univerzita v Liberci",
            "Fakulta umění a architektury",
            "Studentská 1402/2",
            "461 17 Liberec 1",
            "Česká republika",
            "46747885",
            "CZ46747885",
            "fua@tul.cz");

    private static readonly DateTimeOffset IssuedAt =
        new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FuaPay.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FuaPay.slnx was not found.");
    }
}
