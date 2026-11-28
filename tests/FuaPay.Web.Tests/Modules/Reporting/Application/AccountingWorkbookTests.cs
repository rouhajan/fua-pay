using System.Diagnostics;

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

using FuaPay.Web.Modules.Access.Infrastructure.Persistence;
using FuaPay.Web.Modules.Credits.Infrastructure.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;
using FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.Reporting.Infrastructure.Xlsx;
using FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

using Xunit.Abstractions;

namespace FuaPay.Web.Tests.Modules.Reporting.Application;

public sealed class AccountingWorkbookTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("2026-03-29", 23)]
    [InlineData("2026-10-25", 25)]
    [InlineData("2026-12-31", 24)]
    public void PragueBoundariesAreConvertedIndependently(string date, int hours)
    {
        var period = Assert.IsType<AccountingPeriod>(AccountingPeriod.Parse(null, date, date));
        Assert.Equal(TimeSpan.FromHours(hours), period.Until - period.From);
        Assert.Equal(TimeSpan.Zero, period.From.Offset);
        Assert.Equal("2026-11", AccountingPeriod.CurrentMonth(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("2026-3", null, null)]
    [InlineData("2026-03", "2026-03-01", "2026-03-31")]
    [InlineData(null, "2026-03-50", "2026-04-01")]
    [InlineData(null, "2026-04-01", "2026-03-31")]
    [InlineData(null, "2026-03-01", null)]
    [InlineData(null, "9999-01-01", "9999-12-31")]
    public void MalformedOrConflictingPeriodsAreRejected(string? month, string? from, string? to) =>
        Assert.Null(AccountingPeriod.Parse(month, from, to));

    [Fact]
    public void CalculatesDistinctMoneyServiceAndLedgerEvents()
    {
        var source = Example();
        var report = new AccountingReportBuilder(source, Id(61)).Build();

        Assert.Empty(report.Problems);
        Assert.Equal(50_000, report.Received);
        Assert.Equal(24_000, report.Returned);
        Assert.Equal(26_000, report.NetCard);
        Assert.Equal(5, report.Card.Length);
        Assert.DoesNotContain(report.Card, item => item.Id == Id(12) || item.Id == Id(25));
        Assert.Equal(-20_000, report.MovementValue(AccountingMovementKind.CardTopUpReturn));
        Assert.Equal(40_000, report.MovementValue(AccountingMovementKind.CardTopUp));
        Assert.Equal(4_000, report.MovementValue(AccountingMovementKind.SafeQ));
        Assert.Equal(1_500, report.MovementValue(AccountingMovementKind.ManualTopUp));
        Assert.Equal(750, report.MovementValue(AccountingMovementKind.Adjustment));
        Assert.Equal(6_000, report.MovementValue(AccountingMovementKind.JobReturn));
        Assert.True(report.LedgerComplete);
        Assert.Equal(22_000, report.Opening);
        Assert.Equal(46_250, report.Closing);
        Assert.Equal(1, report.CarriedAccounts);
        Assert.Equal(report.Closing, report.Opening + report.MovementTotal);
        Assert.Equal(10_000, report.Centres.Sum(item => item.Card));
        Assert.Equal(6_000, report.Centres.Sum(item => item.Credit));
        Assert.Equal(2_000, report.Centres.Sum(item => item.Print));
        Assert.Equal(10_000, report.Centres.Sum(item => item.Returned));
        Assert.Equal("Plotr F", Assert.Single(report.Centres, item => item.Credit != 0).Name);
        Assert.Equal("Tisk v učebně", Assert.Single(report.Centres, item => item.Print != 0).Name);
        Assert.DoesNotContain(report.Centres, item => item.Id == Id(22));
    }

    [Fact]
    public void ContradictoryLinkedEvidenceAndBrokenLedgerNeverProduceTrustedTotals()
    {
        var source = Example();
        source.Attempts.Single(item => item.Id == Id(33)).ProviderReference = "conflicting";
        source.Movements.Single(item => item.OperationId == Id(41)).Sequence++;
        var report = new AccountingReportBuilder(source, classroomSource: null).Build();

        Assert.False(report.LedgerComplete);
        Assert.Contains(report.Problems, item => item.Code == "LEDGER");
        Assert.Contains(report.Problems, item => item.Code == "RETURN" && item.Id == Id(23));
        Assert.Contains(report.Problems, item => item.Code == "PRINT_SOURCE");
        Assert.DoesNotContain(report.Card, item => item.Id == Id(23));
        Assert.DoesNotContain(report.Centres, item => item.Id == Id(23));
        Assert.Equal("Tisk – nezařazený zdroj", Assert.Single(report.Centres, item => item.Print != 0).Name);
    }

    [Fact]
    public void MissingDocumentIsExplicitAndCashTotalExcludesItsUnverifiedAmount()
    {
        var source = Example();
        var report = new AccountingReportBuilder(source with
        {
            Documents = source.Documents.Where(item => item.SourceId != Id(11)).ToArray()
        }, Id(61)).Build();

        Assert.Equal(10_000, report.Received);
        Assert.Equal(50_000, report.RawTotals.Receipts);
        var problem = Assert.Single(report.Problems, item => item.Code == "DOCUMENT_MISSING");
        Assert.Equal(Id(11), problem.Id);
        Assert.Equal(40_000, problem.Amount);
    }

    [Fact]
    public void WorkbookHasSevenStyledSheetsCorrectFormulaCachesAndTextCells()
    {
        var report = new AccountingReportBuilder(Example(), Id(61)).Build();
        using var stream = new BoundedWorkbookStream();
        var sw = Stopwatch.StartNew();
        new AccountingWorkbookWriter(report, CancellationToken.None).Write(stream);
        output.WriteLine($"Synthetic XLSX: {stream.Length} bytes, writer {sw.ElapsedMilliseconds} ms");
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(document)
            .Select(item => $"{item.Path?.XPath}: {item.Description}").ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));

        var workbook = document.WorkbookPart!;
        var sheets = workbook.Workbook!.Sheets!.Elements<Sheet>().ToArray();
        Assert.Equal(["Platby ČSOB", "Přehled", "Pohyby kreditu", "Zůstatky", "Zakázky", "Kontroly", "Střediska"],
            sheets.Select(sheet => sheet.Name!.Value));
        Assert.Equal(0U, workbook.Workbook!.BookViews!.Elements<WorkbookView>().Single().ActiveTab!.Value);
        Assert.Equal("Datum a čas", Cell(1, "A5").InlineString!.InnerText);
        Assert.Equal("260", Cell(1, "G11").CellValue!.InnerText);
        Assert.Equal("SUM(G6:G10)", Cell(1, "G11").CellFormula!.Text);
        Assert.Equal(2.60m, decimal.Parse(Cell(2, "G12").CellValue!.InnerText, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(462.50m, decimal.Parse(Cell(2, "G24").CellValue!.InnerText, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(CellValues.InlineString, Cell(3, "B6").DataType!.Value);
        Assert.Equal("=1+1", Cell(3, "B6").InlineString!.InnerText);
        Assert.Null(Cell(3, "B6").CellFormula);
        Assert.Equal(5d, Part(1).Worksheet!.Descendants<Pane>().Single().VerticalSplit!.Value);
        Assert.Empty(workbook.ExternalRelationships);
        Assert.Empty(Part(1).HyperlinkRelationships);
        Assert.Equal("'Kontroly'!A40", Part(1).Worksheet!.Descendants<Hyperlink>().First().Location!.Value);

        var target = Environment.GetEnvironmentVariable("FUAPAY_107_SYNTHETIC_XLSX");
        if (!string.IsNullOrWhiteSpace(target))
        {
            using var file = File.Create(target);
            stream.Position = 0;
            stream.CopyTo(file);
        }

        WorksheetPart Part(int sheet) => (WorksheetPart)workbook.GetPartById(sheets[sheet - 1].Id!);
        Cell Cell(int sheet, string address) => Part(sheet).Worksheet!.Descendants<Cell>()
            .Single(cell => cell.CellReference!.Value == address);
    }

    [Fact]
    public void WorkbookMarksWithheldReturnOnAllRelevantSheetsAndDisablesFeeModel()
    {
        var source = Example();
        source.Attempts.Single(item => item.Id == Id(33)).ProviderReference = "conflicting";
        var report = new AccountingReportBuilder(source, Id(61)).Build();
        using var stream = new BoundedWorkbookStream();
        new AccountingWorkbookWriter(report, CancellationToken.None).Write(stream);
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var card = ReadSheet(document, 1);
        var summary = ReadSheet(document, 2);
        var controls = ReadSheet(document, 6);
        var centres = ReadSheet(document, 7);

        Assert.Contains("Neúplné", Value(card, "A4"));
        Assert.Contains("NEÚPLNÉ KARETNÍ PODKLADY", Value(summary, "A4"));
        Assert.Contains("DOLOŽENÁ ČÁST", Value(summary, "A9"));
        Assert.Equal(285m, Number(summary, "G9"));
        foreach (var address in new[] { "G11", "G12", "G13" })
        {
            Assert.Equal("N/A", Value(summary, address));
            Assert.Null(summary.Descendants<Cell>().Single(cell => cell.CellReference!.Value == address).CellFormula);
        }

        Assert.Contains("NEÚPLNÉ FINANČNÍ PODKLADY", Value(centres, "A4"));
        var total = Assert.Single(centres.Descendants<Row>(), row =>
            row.Elements<Cell>().FirstOrDefault()?.InlineString?.InnerText == "DOLOŽENÁ ČÁST");
        Assert.Equal(105m, Number(centres, $"G{total.RowIndex!.Value}"));
        Assert.Contains(controls.Descendants<InlineString>(), item => item.InnerText == "CENTRE_RETURN");
        Assert.Contains(controls.Descendants<InlineString>(), item =>
            item.InnerText.Contains(Id(23).ToString("D"), StringComparison.Ordinal));
        Assert.Empty(new OpenXmlValidator().Validate(document));

        var target = Environment.GetEnvironmentVariable("FUAPAY_107_ACCEPTANCE_XLSX");
        if (!string.IsNullOrWhiteSpace(target))
        {
            using var file = File.Create(target);
            stream.Position = 0;
            stream.CopyTo(file);
        }
    }

    [Fact]
    public void UnallocatedPrintKeepsDulyEvidencedAmountsAndDoesNotDisableCardFeeModel()
    {
        var report = new AccountingReportBuilder(Example(), classroomSource: null).Build();
        using var stream = new MemoryStream();
        new AccountingWorkbookWriter(report, CancellationToken.None).Write(stream);
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var summary = ReadSheet(document, 2);
        var centres = ReadSheet(document, 7);

        Assert.Equal(99m, Number(summary, "G11"));
        Assert.Equal(2.60m, Number(summary, "G12"));
        Assert.DoesNotContain("Neúplné", Value(ReadSheet(document, 1), "A4"));
        Assert.Contains("Tisk bez přiřazení", Value(centres, "A4"));
        Assert.DoesNotContain("NEÚPLNÉ FINANČNÍ PODKLADY", Value(centres, "A4"));
        var total = Assert.Single(centres.Descendants<Row>(), row =>
            row.Elements<Cell>().FirstOrDefault()?.InlineString?.InnerText == "CELKEM");
        Assert.Equal(20m, Number(centres, $"E{total.RowIndex!.Value}"));
        Assert.Equal(80m, Number(centres, $"G{total.RowIndex!.Value}"));
    }

    [Fact]
    public void UnprovenPrintDebitMarksTheCentreSubtotalWithoutChangingCardFees()
    {
        var source = Example();
        source.Prints.Single().AmountMinorUnits++;
        var report = new AccountingReportBuilder(source, Id(61)).Build();
        using var stream = new MemoryStream();
        new AccountingWorkbookWriter(report, CancellationToken.None).Write(stream);
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var summary = ReadSheet(document, 2);
        var centres = ReadSheet(document, 7);

        Assert.Equal(2.60m, Number(summary, "G12"));
        Assert.Contains("NEÚPLNÉ ČLENĚNÍ KREDITU", Value(summary, "A4"));
        Assert.Contains("NEÚPLNÉ FINANČNÍ PODKLADY", Value(centres, "A4"));
        Assert.DoesNotContain("Tisk bez přiřazení", Value(centres, "A4"));
        var total = Assert.Single(centres.Descendants<Row>(), row =>
            row.Elements<Cell>().FirstOrDefault()?.InlineString?.InnerText == "DOLOŽENÁ ČÁST");
        Assert.Equal(0m, Number(centres, $"E{total.RowIndex!.Value}"));
        Assert.Equal(60m, Number(centres, $"G{total.RowIndex!.Value}"));
        Assert.Equal(462.50m, Number(summary, "G24"));
    }

    [Fact]
    public void WorkbookLimitsDocumentCompletenessClaimToLoadedSources()
    {
        var report = new AccountingReportBuilder(Example(), Id(61)).Build();
        using var stream = new MemoryStream();
        new AccountingWorkbookWriter(report, CancellationToken.None).Write(stream);
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var controls = ReadSheet(document, 6);

        Assert.Equal("Kontrola dokladů k načteným zdrojům", Value(controls, "A29"));
        Assert.Contains("Doklady mimo načtené zdroje se nekontrolují.", Value(controls, "C29"));
    }

    private static Worksheet ReadSheet(SpreadsheetDocument document, int sheet)
    {
        var part = document.WorkbookPart!;
        var row = part.Workbook!.Sheets!.Elements<Sheet>().ElementAt(sheet - 1);
        return ((WorksheetPart)part.GetPartById(row.Id!)).Worksheet!;
    }

    private static string Value(Worksheet sheet, string address) =>
        sheet.Descendants<Cell>().SingleOrDefault(cell => cell.CellReference!.Value == address)?.InlineString?.InnerText ?? "";

    private static decimal Number(Worksheet sheet, string address) =>
        decimal.Parse(sheet.Descendants<Cell>().Single(cell => cell.CellReference!.Value == address)
            .CellValue!.InnerText, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void OpenOrCustomPeriodNeverReportsFixedFee()
    {
        var input = Example();
        var report = new AccountingReportBuilder(input with { CapturedAt = At(25) }, Id(61)).Build();
        using var stream = new MemoryStream();
        new AccountingWorkbookWriter(report, CancellationToken.None).Write(stream);
        stream.Position = 0;
        using var xlsx = SpreadsheetDocument.Open(stream, false);
        var workbook = xlsx.WorkbookPart!;
        var sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().ElementAt(1);
        var summary = (WorksheetPart)workbook.GetPartById(sheet.Id!);
        Assert.Equal("N/A", summary.Worksheet!.Descendants<Cell>().Single(cell => cell.CellReference!.Value == "G11")
            .InlineString!.InnerText);
    }

    [Fact]
    public void WorkbookByteLimitCannotBeExceededByWritesOrLengthChanges()
    {
        using var stream = new BoundedWorkbookStream();
        stream.Position = BoundedWorkbookStream.Limit;
        Assert.Throws<AccountingWorkbookLimitException>(() => stream.WriteByte(0));
        Assert.Throws<AccountingWorkbookLimitException>(() => stream.SetLength(BoundedWorkbookStream.Limit + 1));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void JobAllocationUsesSettlementTimeAcrossCardCompletionBoundary()
    {
        var source = Example();
        var period = AccountingPeriod.Parse(null, "2026-03-05", "2026-03-05")!;
        source.Payments.Single(item => item.Id == Id(13)).CompletedAt = At(5).AddHours(12);
        var report = new AccountingReportBuilder(source with
        {
            Period = period,
            Cutoff = period.Until,
            RawTotals = new(0, 0, 0, 0)
        }, Id(61)).Build();

        Assert.Empty(report.Card);
        Assert.Equal(1, report.CrossBoundaryEffects);
        Assert.Equal(10_000, Assert.Single(report.Centres, item => item.Card != 0).Card);
        Assert.Equal(6_000, Assert.Single(report.Centres, item => item.Credit != 0).Credit);
    }

    [Fact]
    public void EmptyPeriodKeepsAllSheetsAndUsesZeroEventTotals()
    {
        var source = Example();
        var period = AccountingPeriod.Parse("2026-01", null, null)!;
        var report = new AccountingReportBuilder(source with
        {
            Period = period,
            Cutoff = period.Until,
            RawTotals = new(0, 0, 0, 0)
        }, Id(61)).Build();

        Assert.Empty(report.Card);
        Assert.Empty(report.Movements);
        Assert.Equal(0, report.Closing);
        using var stream = new BoundedWorkbookStream();
        new AccountingWorkbookWriter(report, CancellationToken.None).Write(stream);
        stream.Position = 0;
        using var workbook = SpreadsheetDocument.Open(stream, false);
        Assert.Equal(7, workbook.WorkbookPart!.Workbook!.Sheets!.ChildElements.Count);
        Assert.Empty(new OpenXmlValidator().Validate(workbook));
    }

    [Fact]
    public void SyntheticVolumeReportsWriterTimeAllocationAndFileSize()
    {
        var source = Example();
        var report = new AccountingReportBuilder(source, Id(61)).Build();
        var sample = report.Movements[0];
        var expanded = report with
        {
            Movements = Enumerable.Range(0, 5_000)
                .Select(i => sample with { Id = Guid.NewGuid(), Sequence = i + 1 }).ToArray()
        };
        using var stream = new BoundedWorkbookStream();
        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        new AccountingWorkbookWriter(expanded, CancellationToken.None).Write(stream);
        Assert.InRange(stream.Length, 20_000, BoundedWorkbookStream.Limit);
        output.WriteLine($"Volume: 5000 synthetic movement rows. XLSX {stream.Length} bytes. Writer {watch.ElapsedMilliseconds} ms. Managed allocations on writer thread {GC.GetAllocatedBytesForCurrentThread() - bytesBefore} bytes.");
    }

    private static AccountingSnapshot Example()
    {
        var period = AccountingPeriod.Parse("2026-03", null, null)!;
        return new AccountingSnapshot(period, period.Until.AddDays(2), period.Until,
            new(2, 50_000, 3, 24_000),
            [new CreditAccountEntity { Id = Id(1), OwnerId = Id(2), BalanceMinorUnits = 43_250 },
             new CreditAccountEntity { Id = Id(3), OwnerId = Id(4), BalanceMinorUnits = 2_000 }],
            [Move(12, 0, 20_000, 1, 20_000), Move(11, 3, 40_000, 2, 60_000), Move(22, 4, -20_000, 3, 40_000),
             Move(41, 5, -6_000, 4, 34_000), Move(42, 6, -2_000, 5, 32_000),
             Move(26, 7, 6_000, 6, 38_000), Move(43, 8, 4_000, 7, 42_000), Move(44, 9, 1_500, 8, 43_500),
             Move(45, 10, 750, 9, 44_250), Move(46, 50, -1_000, 10, 43_250),
             new CreditMovementEntity { AccountId = Id(3), OperationId = Id(47), Sequence = 1, MovementType = 1,
                AmountMinorUnits = 2_000, BalanceAfterMinorUnits = 2_000, RecordedAt = At(0), Description = "Přenos" }],
            [Payment(11, 3, 40_000), Payment(12, 0, 20_000), Payment(13, 5, 10_000, job: 40),
             new PaymentEntity { Id = Id(14), Provider = 1, Status = 3, CompletedAt = At(5), AmountMinorUnits = 999_000 }],
            [new(Id(11), 2, 3, 1011), new(Id(12), 2, 3, 1012), new(Id(13), 2, 3, 1013)],
            [],
            [Return(22, 3, 4, 20_000, payment: 12), Return(23, 1, 7, 2_500, job: 40, payment: 13),
             Return(24, 1, 8, 1_500, job: 40, payment: 13), Return(25, 1, 9, 7_000, job: 40, payment: 13, state: 4),
             Return(26, 2, 7, 6_000, job: 41)],
            [Attempt(32, 22, 12, 4, operation: 1), Attempt(33, 23, 13, 7), Attempt(34, 24, 13, 8),
             Attempt(36, 24, 13, 8, operation: 1, state: 4)],
            [Job(40, 60, 5, 10_000, 2, 13), Job(41, 62, 5, 6_000, 1, 41)],
            [new ServiceUnitEntity { Id = Id(60), Code = "XNEW", DisplayName = "Dynamické pracoviště", Status = 1 },
             new ServiceUnitEntity { Id = Id(62), Code = "PLTF", DisplayName = "Plotr F", Status = 2 }],
            [Document(11, 3, 40_000), Document(12, 0, 20_000), Document(13, 5, 10_000, job: 40)],
            [new ManualCreditTopUpCommandEntity { CommandId = Id(44), OwnerId = Id(2), AmountMinorUnits = 1_500,
                AcceptedAt = At(9), FinancialDocumentRequired = false }],
            [new LegacySafeQCreditTransferEntity { CommandId = Id(43), OwnerId = Id(2), AmountMinorUnits = 4_000, AcceptedAt = At(8) }],
            [new CreditAdjustmentCommandEntity { CommandId = Id(45), OwnerId = Id(2), SignedAmountMinorUnits = 750, AcceptedAt = At(10) }],
            [new PrintReservationEntity { Id = Id(63), CreditAccountId = Id(1), PrintSourceId = Id(61),
                Status = 3, AmountMinorUnits = 2_000, DebitOperationId = Id(42), StateChangedAt = At(6) }],
            [new AccessUserEntity { Id = Id(2), DisplayName = "=1+1", Email = "synthetic@example.test" },
             new AccessUserEntity { Id = Id(4), DisplayName = "Účet s přenosem" }]);
    }

    private static CreditMovementEntity Move(int id, int day, long signed, int seq, long balance) => new()
    {
        OperationId = Id(id),
        AccountId = Id(1),
        RecordedAt = At(day),
        AmountMinorUnits = Math.Abs(signed),
        MovementType = signed > 0 ? 1 : 2,
        Sequence = seq,
        BalanceAfterMinorUnits = balance,
        Description = "Syntetická operace"
    };

    private static PaymentEntity Payment(int id, int day, long amount, int? job = null) => new()
    {
        Id = Id(id),
        Provider = 2,
        Status = 3,
        PurposeType = job.HasValue ? 2 : 1,
        CustomerUserId = Id(2),
        ProviderReference = $"synthetic{id}",
        JobId = job.HasValue ? Id(job.Value) : null,
        AmountMinorUnits = amount,
        CreatedAt = At(day).AddMinutes(-1),
        CompletedAt = At(day).AddSeconds(1)
    };

    private static JobEntity Job(int id, int unit, int day, long price, int method, int reference) => new()
    {
        Id = Id(id),
        ServiceUnitId = Id(unit),
        Number = "TEST-" + id,
        Title = "Syntetická zakázka",
        CustomerUserId = Id(2),
        PriceMinorUnits = price,
        PaymentStatus = 2,
        ProductionStatus = 2,
        SettlementType = method,
        SettlementReferenceId = Id(reference),
        CreatedAt = At(day).AddHours(-1),
        SettledAt = At(day)
    };

    private static FinancialDocumentEntity Document(int id, int day, long amount, int? job = null)
    {
        var tax = FinancialDocumentTaxPolicy.CreateApprovedSnapshot(amount);
        return new FinancialDocumentEntity
        {
            DocumentId = Id(100 + id),
            SourceId = Id(id),
            SourceType = (int)FinancialDocumentSourceType.Payment,
            CustomerUserId = Id(2),
            CustomerDisplayName = "Synthetic payer",
            Currency = "CZK",
            FinancialEventAt = At(day),
            IssuedAt = At(day).AddSeconds(2),
            DocumentNumber = $"FUA-2026-{id:000000}",
            DocumentType = job.HasValue ? 3 : 2,
            AmountMinorUnits = amount,
            SettlementMethod = 2,
            Provider = "Csob",
            ProviderReference = $"synthetic{id}",
            ProviderOrderNumber = (1000 + id).ToString(System.Globalization.CultureInfo.InvariantCulture),
            JobId = job.HasValue ? Id(job.Value) : null,
            JobNumber = job.HasValue ? "TEST-" + job : null,
            JobTitle = job.HasValue ? "Synthetic job" : null,
            JobDescription = job.HasValue ? "Synthetic description" : null,
            ServiceUnitName = job.HasValue ? "Synthetic centre" : null,
            SchemaVersion = 2,
            RenderVersion = 2,
            IssuerLegalName = "Test organization",
            IssuerUnitName = "Test faculty",
            IssuerAddressLine1 = "Test avenue",
            IssuerAddressLine2 = "Test city",
            IssuerCountry = "CZ",
            IssuerRegistrationNumber = "00000000",
            IssuerVatNumber = "CZ00000000",
            IssuerContactEmail = "owner@example.test",
            TaxTreatment = (int)tax.Treatment,
            VatRateBasisPoints = tax.VatRateBasisPoints,
            TaxBaseMinorUnits = tax.TaxBaseMinorUnits,
            VatAmountMinorUnits = tax.VatAmountMinorUnits
        };
    }

    private static SettlementReturnEntity Return(int id, int kind, int day, long amount,
        int? job = null, int? payment = null, int state = 3) => new()
        {
            Id = Id(id),
            OriginalPaymentId = payment.HasValue ? Id(payment.Value) : null,
            JobId = job.HasValue ? Id(job.Value) : null,
            Kind = kind,
            State = state,
            CompletedAt = At(day),
            RequestedAt = At(day).AddHours(-1),
            Currency = "CZK",
            CustomerUserId = Id(2),
            AmountMinorUnits = amount
        };

    private static SettlementReturnProviderAttemptEntity Attempt(int id, int refund, int payment,
        int day, int operation = 2, int state = 3) => new()
        {
            Id = Id(id),
            SettlementReturnId = Id(refund),
            Provider = 2,
            ProviderReference = $"synthetic{payment}",
            StartedAt = At(day).AddMinutes(-1),
            FinishedAt = At(day).AddSeconds(1),
            State = state,
            Operation = operation
        };

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:000000000000}");
    private static DateTimeOffset At(int day) => new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero).AddDays(day - 1);
}
