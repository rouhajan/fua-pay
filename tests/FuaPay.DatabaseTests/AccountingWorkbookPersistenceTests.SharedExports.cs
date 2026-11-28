using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PdfSharp.Pdf.IO;

namespace FuaPay.DatabaseTests;

public sealed partial class AccountingWorkbookPersistenceTests
{
    [Fact]
    public async Task SharedExportsPageKeepsPeriodsFormatsAndAuditIndependent()
    {
        const string archiveHandler = "FinancialDocuments";
        var owner = Guid.NewGuid();
        const string note = "Synthetic shared export opening";
        const string number = "FUA-2026-989107";
        var archiveDate = PostedAt.AddDays(1);
        var customer = new FinancialDocumentCustomerSnapshot(owner, "Synthetic shared export customer", null);
        var issuer = new FinancialDocumentIssuerSnapshot("Test University", "Test Faculty", "Test Street",
            "Test City", "CZ", "00000000", "CZ00000000", "issuer@example.test");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<FuaPayDbContext>();
        var repository = services.GetRequiredService<IFinancialDocumentRepository>();
        var legacyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        try
        {
            await SeedAsync(owner, 2_500, note);
            var operation = await db.Database.SqlQuery<Guid>($"""
                SELECT m.operation_id AS "Value"
                FROM credits.movements m JOIN credits.accounts a ON a.id = m.account_id
                WHERE a.owner_id = {owner}
                """).SingleAsync();
            var document = FinancialDocument.CreateManualCreditTopUp(documentId, number, operation, customer,
                2_500, "CZK", PostedAt, archiveDate, issuer, FinancialDocumentTaxPolicy.CreateApprovedSnapshot(2_500));
            repository.Stage(document);
            await db.SaveChangesAsync();
            var before = await ReadFinancialStateAsync();

            using var app = CreateApp(owner);
            using var client = CreateClient(app, owner);
            var html = await client.GetStringAsync(Exports);
            var forms = Regex.Matches(html, @"<form\b[^>]*>[\s\S]*?</form>", RegexOptions.CultureInvariant)
                .Select(match => match.Value).ToArray();
            var csvForm = Assert.Single(forms, form => form.Contains("?handler=Reconciliation", StringComparison.Ordinal));
            var workbookForm = Assert.Single(forms, form => form.Contains("?handler=AccountingWorkbook", StringComparison.Ordinal));
            var archiveForms = forms.Where(form => form.Contains("?handler=FinancialDocuments", StringComparison.Ordinal)).ToArray();
            Assert.True(archiveForms.Length > 0, "The FinancialDocuments form is missing from the shared exports page.");
            var archiveForm = Assert.Single(archiveForms);
            Assert.Contains("name=\"from\"", csvForm, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Archive", csvForm, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Workbook", csvForm, StringComparison.Ordinal);
            Assert.Contains("name=\"WorkbookMonth\"", workbookForm, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Archive", workbookForm, StringComparison.Ordinal);
            Assert.Contains("name=\"ArchiveFrom\"", archiveForm, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Workbook", archiveForm, StringComparison.Ordinal);
            Assert.All(new[] { csvForm, workbookForm, archiveForm }, form =>
                Assert.Contains("name=\"__RequestVerificationToken\"", form, StringComparison.Ordinal));
            var token = await TokenAsync(client);

            using (var invalid = await PostAsync(archiveHandler,
                       ("ArchiveFrom", "not-a-date"), ("ArchiveTo", "2026-02-13"), ("WorkbookMonth", "")))
            {
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                var page = await invalid.Content.ReadAsStringAsync();
                Assert.Contains("value=\"not-a-date\"", page, StringComparison.Ordinal);
                Assert.Contains("31 dní", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
                Assert.Contains("name=\"WorkbookMonth\"", page, StringComparison.Ordinal);
            }

            using (var invalid = await PostAsync("AccountingWorkbook",
                       ("WorkbookFrom", "2026-02-12"), ("WorkbookTo", "not-a-date"), ("ArchiveMonth", "2026-02")))
            {
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                var page = await invalid.Content.ReadAsStringAsync();
                Assert.Contains("value=\"not-a-date\"", page, StringComparison.Ordinal);
                Assert.Contains("obě data vlastního období", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
                Assert.Contains("name=\"ArchiveMonth\"", page, StringComparison.Ordinal);
            }

            using var zipResponse = await PostAsync(archiveHandler, ("ArchiveFrom", "2026-02-13"),
                ("ArchiveTo", "2026-02-13"), ("WorkbookMonth", "bad-month"));
            AssertDownload(zipResponse, "application/zip");
            Assert.Equal("doklady-o-uhrade-2026-02-13--2026-02-13.zip",
                zipResponse.Content.Headers.ContentDisposition?.FileNameStar);
            using var archive = new ZipArchive(new MemoryStream(await zipResponse.Content.ReadAsByteArrayAsync()));
            var pdf = Assert.Single(archive.Entries);
            Assert.Equal($"doklad-o-uhrade-{number}.pdf", pdf.FullName);
            using var pdfStream = new MemoryStream();
            await using (var entry = pdf.Open())
            {
                await entry.CopyToAsync(pdfStream);
            }

            pdfStream.Position = 0;
            using var archivedPdf = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
            using var individual = await client.GetAsync($"/Admin/FinancialDocuments/{documentId}/document.pdf");
            AssertDownload(individual, "application/pdf");
            using var individualPdf = PdfReader.Open(new MemoryStream(await individual.Content.ReadAsByteArrayAsync()),
                PdfDocumentOpenMode.Import);
            Assert.Equal(1, archivedPdf.PageCount);
            Assert.Equal(archivedPdf.PageCount, individualPdf.PageCount);
            Assert.Contains(number, archivedPdf.Info.Title, StringComparison.Ordinal);
            Assert.Equal(individualPdf.Info.Title, archivedPdf.Info.Title);
            Assert.Equal(individualPdf.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue,
                archivedPdf.Pages[0].Contents.CreateSingleContent().Stream.UnfilteredValue);

            var legacy = new FinancialDocument(legacyId, "FUA-2026-989108", FinancialDocumentType.ManualCreditTopUp,
                FinancialDocumentSourceType.ManualCreditTopUp, Guid.NewGuid(), customer, 2_500, "CZK",
                PostedAt, archiveDate.AddMinutes(1), FinancialDocumentSettlementMethod.ManualCreditTopUp,
                issuer, null, null, null, 1, 1);
            repository.Stage(legacy);
            await db.SaveChangesAsync();
            var withLegacy = await ReadFinancialStateAsync();
            using (var mixed = await PostAsync(archiveHandler, ("ArchiveFrom", "2026-02-13"), ("ArchiveTo", "2026-02-13")))
            {
                Assert.Equal(HttpStatusCode.Conflict, mixed.StatusCode);
                Assert.Null(mixed.Content.Headers.ContentDisposition);
            }

            using var workbookResponse = await PostAsync("AccountingWorkbook", ("WorkbookFrom", "2026-02-12"),
                ("WorkbookTo", "2026-02-12"), ("ArchiveFrom", "bad-date"));
            AssertDownload(workbookResponse, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            using var workbook = new ZipArchive(new MemoryStream(await workbookResponse.Content.ReadAsByteArrayAsync()));
            Assert.Contains(note, Xml(workbook, "xl/worksheets/sheet3.xml").ToString(), StringComparison.Ordinal);

            foreach (var handler in new[] { "Jobs", "Credit", "Payments", "Reconciliation" })
            {
                using var csv = await PostAsync(handler, ("from", "2026-02-12"), ("to", "2026-02-12"),
                    ("WorkbookMonth", "bad-month"), ("ArchiveFrom", "bad-date"));
                AssertDownload(csv, "text/csv");
                if (handler == "Credit")
                {
                    Assert.Contains(note, await csv.Content.ReadAsStringAsync(), StringComparison.Ordinal);
                }
            }

            Assert.Equal(withLegacy, await ReadFinancialStateAsync());
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM financial_documents.documents WHERE document_id = {legacyId}");
            Assert.Equal(before, await ReadFinancialStateAsync());
            var audit = await db.Database.SqlQuery<string>($"""
                SELECT action AS "Value" FROM audit.events
                WHERE actor_user_id = {owner} ORDER BY action
                """).ToArrayAsync();
            Assert.Equal(["export.accounting-workbook", "export.credit-movements", "export.jobs",
                "export.payment-reconciliation", "export.payments"], audit);

            async Task<HttpResponseMessage> PostAsync(string handler, params (string Key, string Value)[] fields)
            {
                using var form = new FormUrlEncodedContent(fields.Select(field =>
                    KeyValuePair.Create(field.Key, field.Value)).Append(new("__RequestVerificationToken", token)));
                return await client.PostAsync(Exports + "?handler=" + handler, form);
            }

            async Task<string[]> ReadFinancialStateAsync() =>
                await db.Database.SqlQuery<string>($"""
                    SELECT to_jsonb(d)::text AS "Value" FROM financial_documents.documents d
                        WHERE d.customer_user_id = {owner}
                    UNION ALL SELECT to_jsonb(m)::text FROM credits.movements m
                        JOIN credits.accounts a ON a.id = m.account_id WHERE a.owner_id = {owner}
                    UNION ALL SELECT to_jsonb(a)::text FROM credits.accounts a WHERE a.owner_id = {owner}
                    UNION ALL SELECT to_jsonb(n)::text FROM financial_documents.number_counters n
                        WHERE n.business_year = 2026
                    ORDER BY 1
                    """).ToArrayAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM financial_documents.documents WHERE document_id IN ({documentId}, {legacyId})");
            await DeleteFixturesAsync(owner);
        }
    }

    private static void AssertDownload(HttpResponseMessage response, string mediaType)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
