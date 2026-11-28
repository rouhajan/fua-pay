using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed partial class AccountingWorkbookPersistenceTests
{
    private static readonly XNamespace SheetXml = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static int _reportOrder = 107000;

    [Theory]
    [InlineData(false, 40, 360, 80)]
    [InlineData(true, 15, 385, 105)]
    public async Task WorkbookProjectsCanonicalGraphAcrossPragueBoundary(
        bool conflictingReturn, int returnedCzk, int netCardCzk, int netCentresCzk)
    {
        var scenario = new ReportGraph(Guid.NewGuid());
        try
        {
            await using (var setup = factory.Services.CreateAsyncScope())
            {
                await SeedCanonicalGraphAsync(setup.ServiceProvider, scenario, conflictingReturn);
            }

            var before = await FinancialFingerprintAsync(scenario);
            var writes = new ReportWriteObserver();
            using var authenticated = CreateApp(scenario.Owner);
            using var app = authenticated.WithWebHostBuilder(builder => builder
                .UseSetting("AccountingWorkbook:ClassroomPrintSourceId", scenario.PrintSource.ToString("D"))
                .ConfigureTestServices(services =>
                    services.ConfigureDbContext<FuaPayDbContext>(options => options.AddInterceptors(writes))));
            using var client = CreateClient(app, scenario.Owner);
            var token = await TokenAsync(client);
            using var form = new FormUrlEncodedContent([
                new("WorkbookMonth", "2026-03"), new("__RequestVerificationToken", token)]);
            using var response = await client.PostAsync(Handler, form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.NotNull(response.Content.Headers.ContentDisposition);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var workbook = new ZipArchive(new MemoryStream(bytes));

            var card = Worksheet(workbook, 1);
            var summary = Worksheet(workbook, 2);
            var moves = Worksheet(workbook, 3);
            var balances = Worksheet(workbook, 4);
            var jobs = Worksheet(workbook, 5);
            var controls = Worksheet(workbook, 6);
            var centres = Worksheet(workbook, 7);

            var cardTotal = FindRow(card, conflictingReturn ? "CELKEM DOLOŽENÉ" : "CELKEM");
            Assert.Equal(400m, cardTotal.Money("E"));
            Assert.Equal(returnedCzk, cardTotal.Money("F"));
            Assert.Equal(netCardCzk, cardTotal.Money("G"));
            Assert.Equal(1, card.Rows.Count(row => row.Value("D") == scenario.WalletRef));
            Assert.DoesNotContain(card.Rows, row => row.Value("D") == scenario.BoundaryRef);
            Assert.Equal(conflictingReturn ? 1 : 2, card.Rows.Count(row => row.Value("D") == scenario.PriorRef));
            Assert.Contains(card.Rows, row => row.Value("D") == scenario.PriorRef && row.Money("F") == 15m);
            Assert.Equal(!conflictingReturn, card.Rows.Any(row =>
                row.Value("D") == scenario.PriorRef && row.Money("F") == 25m));

            Assert.Equal(15m, summary.Money("G16"));
            Assert.Equal(400m, summary.Money("G17"));
            Assert.Equal(0m, summary.Money("G18"));
            Assert.Equal(40m, summary.Money("G19"));
            Assert.Equal(-60m, summary.Money("G20"));
            Assert.Equal(-20m, summary.Money("G21"));
            Assert.Equal(450m, summary.Money("G23"));
            Assert.Equal(450m, summary.Money("G24"));
            Assert.Equal(450m, FindRow(balances, "ZŮSTATKY CELKEM").Money("D"));

            Assert.Equal(6, moves.Rows.Count(row => Guid.TryParse(row.Value("H"), out _)));
            AssertMovement(scenario.Wallet, "Dobití kartou", 400m);
            AssertMovement(scenario.CreditJob, "Úhrada zakázky", -60m);
            AssertMovement(scenario.PrintDebit, "Tisk", -20m);
            AssertMovement(scenario.CreditReturn, "Vratka zakázky", 60m);
            AssertMovement(scenario.Manual, "Ruční dobití", 15m);
            AssertMovement(scenario.SafeQ, "Převod SafeQ", 40m);
            Assert.DoesNotContain(moves.Rows, row => row.Value("H") == scenario.AfterCut.ToString("D"));
            Assert.Equal(2, jobs.Rows.Count(row => row.Value("F") == "Uhrazeno"));

            var centreTotal = FindRow(centres, conflictingReturn ? "DOLOŽENÁ ČÁST" : "CELKEM");
            Assert.Equal(100m, centreTotal.Money("C"));
            Assert.Equal(60m, centreTotal.Money("D"));
            Assert.Equal(20m, centreTotal.Money("E"));
            Assert.Equal(returnedCzk + 60m, centreTotal.Money("F"));
            Assert.Equal(netCentresCzk, centreTotal.Money("G"));
            Assert.Equal(100m, Assert.Single(centres.Rows, row => row.Value("B") == scenario.BoundaryUnitCode).Money("C"));
            var history = Assert.Single(centres.Rows, row => row.Value("B") == scenario.HistoricalUnitCode);
            Assert.Equal(60m, history.Money("D"));
            Assert.Equal(returnedCzk + 60m, history.Money("F"));
            Assert.Equal(20m, Assert.Single(centres.Rows, row =>
                row.Value("A") == "Tisk v učebně" && row.Value("H") == "1").Money("E"));
            Assert.Contains(centres.Rows, row => row.Value("I") == scenario.PrintSource.ToString("D") &&
                row.Value("H").StartsWith(scenario.PrintDebit.ToString("D"), StringComparison.Ordinal));
            Assert.DoesNotContain(centres.Rows, row => row.Value("H").Contains(scenario.RejectedReturn.ToString("D"), StringComparison.Ordinal));

            Assert.Contains(controls.Rows, row => row.Value("A") == scenario.GoodFallbackReturn.ToString("D") &&
                row.Value("C").Contains(scenario.PriorPayment.ToString("D"), StringComparison.Ordinal));
            Assert.Equal(!conflictingReturn, controls.Rows.Any(row => row.Value("A") == scenario.OtherReturn.ToString("D") &&
                row.Value("C").Contains(scenario.PriorPayment.ToString("D"), StringComparison.Ordinal)));
            Assert.DoesNotContain(controls.Rows, row => row.Value("A") == scenario.RejectedReturn.ToString("D"));
            Assert.Equal(1m, FindRow(controls, "Účinky na hranici období").Money("B"));
            Assert.Equal(conflictingReturn ? 25m : 0m, FindRow(controls, "Rozdíl vratek po kontrole vazeb").Money("B"));
            if (conflictingReturn)
            {
                Assert.Equal("N/A", summary.Value("G12"));
                Assert.Contains("NEÚPLNÉ", summary.Value("A4"));
                Assert.Contains("NEÚPLNÉ", centres.Value("A4"));
                Assert.Contains(controls.Rows, row => row.Value("A") == "CENTRE_RETURN" &&
                    row.Value("C").Contains(scenario.OtherReturn.ToString("D"), StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal(3.60m, summary.Money("G12"));
                Assert.Equal("", summary.Value("A4"));
            }

            Assert.Equal(1, writes.AuditWrites);
            Assert.Empty(writes.OtherWrites);
            Assert.Equal(before, await FinancialFingerprintAsync(scenario));
            output.WriteLine($"Graph: conflict={conflictingReturn}, bank net {netCardCzk}, centre net {netCentresCzk}, output {bytes.Length} bytes. Audit writes: 1. Financial writes: 0.");

            void AssertMovement(Guid id, string label, decimal amount)
            {
                var row = Assert.Single(moves.Rows, candidate => candidate.Value("H") == id.ToString("D"));
                Assert.Equal(label, row.Value("D"));
                Assert.Equal(amount, row.Money("E"));
            }
        }
        finally
        {
            await DeleteCanonicalGraphAsync(scenario);
        }
    }

    private static async Task SeedCanonicalGraphAsync(IServiceProvider services, ReportGraph graph, bool conflictingReturn)
    {
        using var budget = new CancellationTokenSource(Timeout);
        var ct = budget.Token;
        var setupAt = At(2, 1);
        var profile = new AccessUser(graph.Owner, "Report fixture", "report@example.test", setupAt);
        await services.GetRequiredService<IAccessUserRepository>().AddAsync(profile,
            new ExternalIdentityKey("report-test", "synthetic", graph.Owner.ToString("N")), ct);
        var actor = ServiceUnitChangeActor.ForProcess("accounting-workbook-test");
        var newUnit = new ServiceUnit(graph.BoundaryUnit, graph.BoundaryUnitCode, "Syntetický Plotr A",
            ServiceType.LargeFormatPrint, setupAt, actor);
        var oldUnit = new ServiceUnit(graph.HistoricalUnit, graph.HistoricalUnitCode, "Syntetický Plotr F",
            ServiceType.LargeFormatPrint, setupAt, actor);
        oldUnit.Deactivate(At(4, 2), actor);
        var unitRepo = services.GetRequiredService<IServiceUnitRepository>();
        await unitRepo.AddAsync(newUnit, ct);
        await unitRepo.AddAsync(oldUnit, ct);

        var marchTopUp = At(3, 3);
        var beforeMidnight = At(3, 31, 21, 59, 59);
        var afterMidnight = beforeMidnight.AddSeconds(2);
        var jobCreditAt = At(3, 5);
        var printAt = At(3, 6);
        var creditReturnAt = At(3, 9);
        var manualAt = At(3, 10);
        var safeQAt = At(3, 11);
        await AddJobAsync(graph.PriorJob, oldUnit, 15_000, JobSettlementType.DirectPayment, graph.PriorPayment, At(2, 12));
        await AddJobAsync(graph.BoundaryJob, newUnit, 10_000, JobSettlementType.DirectPayment, graph.BoundaryPayment, beforeMidnight);
        await AddJobAsync(graph.CreditJob, oldUnit, 6_000, JobSettlementType.Credit, graph.CreditJob, jobCreditAt);
        await AddPaidAsync(graph.PriorPayment, graph.PriorRef, 15_000, At(2, 12), At(2, 12).AddSeconds(1), graph.PriorJob, oldUnit);
        await AddPaidAsync(graph.BoundaryPayment, graph.BoundaryRef, 10_000, beforeMidnight, afterMidnight, graph.BoundaryJob, newUnit);
        await AddPaidAsync(graph.Wallet, graph.WalletRef, 40_000, marchTopUp, marchTopUp.AddSeconds(1), null, null);

        var account = new CreditAccount(graph.Account, graph.Owner);
        account.Credit(Guid.NewGuid(), new Money(1_500), At(2, 10), "Synthetic opening");
        account.Credit(graph.Wallet, new Money(40_000), marchTopUp, "Synthetic wallet");
        account.Debit(graph.CreditJob, new Money(6_000), jobCreditAt, "Synthetic job payment");
        account.Debit(graph.PrintDebit, new Money(2_000), printAt, "Synthetic capture");
        account.Credit(graph.CreditReturn, new Money(6_000), creditReturnAt, "Synthetic compensation");
        account.Credit(graph.Manual, new Money(1_500), manualAt, "Synthetic manual");
        account.Credit(graph.SafeQ, new Money(4_000), safeQAt, "Synthetic SafeQ");
        account.Credit(graph.AfterCut, new Money(750), At(4, 2), "Synthetic after March");
        await services.GetRequiredService<ICreditAccountRepository>().AddAsync(account, ct);

        var printRepo = services.GetRequiredService<IPrintReservationRepository>();
        var captured = Reserve(2_000);
        captured.Capture(Guid.NewGuid(), graph.PrintDebit, printAt);
        var released = Reserve(9_100);
        released.Release(Guid.NewGuid(), printAt);
        await printRepo.AddAsync(captured, ct);
        await printRepo.AddAsync(released, ct);
        await printRepo.AddAsync(Reserve(9_000), ct);

        await AddReturnAsync(graph.OtherReturn, 2_500, At(3, 7), corrupted: conflictingReturn);
        await AddReturnAsync(graph.GoodFallbackReturn, 1_500, At(3, 8), fallback: true);
        await AddReturnAsync(graph.RejectedReturn, 7_000, At(3, 8), rejected: true);
        var compensation = new SettlementReturn(graph.CreditReturn, Guid.NewGuid(), SettlementReturnKind.CreditJob,
            null, graph.CreditJob, graph.Owner, graph.Owner, new Money(6_000), "Synthetic compensation", creditReturnAt.AddMinutes(-2));
        compensation.Begin(creditReturnAt.AddMinutes(-1));
        compensation.Complete(creditReturnAt);
        await services.GetRequiredService<ISettlementReturnRepository>().AddAsync(compensation, ct);

        var db = services.GetRequiredService<FuaPayDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO credits.manual_topup_commands
              (command_id, administrator_user_id, owner_id, amount_minor_units, note, accepted_at, financial_document_required)
            VALUES ({graph.Manual}, {graph.Owner}, {graph.Owner}, 1500, 'Synthetic', {manualAt}, FALSE);
            INSERT INTO credits.legacy_safeq_credit_transfers
              (command_id, administrator_user_id, owner_id, safeq_user_id, snapshot_sha256, amount_minor_units, accepted_at)
            VALUES ({graph.SafeQ}, {graph.Owner}, {graph.Owner}, {graph.Owner.ToString("N")}, {new string('A', 64)}, 4000, {safeQAt});
            """, ct);
        return;

        PrintReservation Reserve(long amount) => new(Guid.NewGuid(), graph.Account, graph.PrintSource,
            $"urn:uuid:{Guid.NewGuid():D}", new Money(amount), Guid.NewGuid(), printAt.AddMinutes(-1));

        async Task AddJobAsync(Guid id, ServiceUnit unit, long amount, JobSettlementType type, Guid operation, DateTimeOffset at)
        {
            var job = new Job(id, TestJobData.NextJobNumber(), unit.Id, graph.Owner, graph.Owner,
                ServiceType.LargeFormatPrint, "Syntetická zakázka", "Účetní fixture", new Money(amount), at.AddMinutes(-5));
            job.Publish(at.AddMinutes(-4));
            job.ConfirmSettlement(type, operation, at);
            Assert.Equal(JobProductionStatus.Published, job.ProductionStatus);
            await services.GetRequiredService<IJobRepository>().AddAsync(job, ct);
        }

        async Task AddPaidAsync(Guid id, string payId, long amount, DateTimeOffset settled, DateTimeOffset completed,
            Guid? jobId, ServiceUnit? unit)
        {
            var created = settled.AddMinutes(-4);
            var payment = new Payment(id, graph.Owner, jobId.HasValue ? PaymentPurposeType.Job : PaymentPurposeType.CreditTopUp,
                jobId, new Money(amount), PaymentProvider.Csob, created, jobId.HasValue ? null : Guid.NewGuid());
            var order = Interlocked.Increment(ref _reportOrder);
            var init = new PaymentInitiation(id, PaymentProvider.Csob, order, Guid.NewGuid(), created);
            init.Begin(created.AddSeconds(1));
            init.Complete(created.AddSeconds(2));
            var payments = services.GetRequiredService<IPaymentRepository>();
            await payments.AddPreparedAsync(payment, init, ct);
            payment.MarkPending(payId, created.AddSeconds(2));
            payment.Complete(completed);
            await payments.SaveAsync(payment, ct);

            var issued = completed.AddSeconds(1);
            var number = $"FUA-2026-{order:000000}";
            var customer = new FinancialDocumentCustomerSnapshot(graph.Owner, profile.DisplayName, profile.Email);
            var authority = services.GetRequiredService<IFinancialDocumentIssuanceProfile>();
            var issuer = authority.CreateIssuerSnapshot();
            var tax = authority.CreateTaxSnapshot(amount);
            var provider = new FinancialDocumentProviderSnapshot("Csob", payId, order.ToString(CultureInfo.InvariantCulture));
            FinancialDocument document;
            if (jobId is { } job)
            {
                var origin = await services.GetRequiredService<IJobRepository>().FindByIdAsync(job, ct)
                    ?? throw new InvalidOperationException("Synthetic job not persisted.");
                document = FinancialDocument.CreateDirectJobCardPayment(Guid.NewGuid(), number, id, customer, amount,
                    "CZK", settled, issued, issuer, tax, provider,
                    new FinancialDocumentJobSnapshot(origin.Id, origin.Number, origin.Title, origin.Description, unit!.DisplayName));
            }
            else
            {
                document = FinancialDocument.CreateCardWalletTopUp(Guid.NewGuid(), number, id, customer, amount,
                    "CZK", settled, issued, issuer, tax, provider);
            }

            var documents = services.GetRequiredService<IFinancialDocumentRepository>();
            documents.Stage(document);
            await documents.PersistStagedAsync(document, ct);
        }

        async Task AddReturnAsync(Guid id, long amount, DateTimeOffset at,
            bool fallback = false, bool rejected = false, bool corrupted = false)
        {
            var result = new SettlementReturn(id, Guid.NewGuid(), SettlementReturnKind.CardJob, graph.PriorPayment,
                graph.PriorJob, graph.Owner, graph.Owner, new Money(amount), "Synthetic partial refund", at.AddMinutes(-2));
            result.Begin(at.AddMinutes(-1));
            if (rejected)
            {
                result.Reject(at);
            }
            else
            {
                result.Complete(at);
            }

            await services.GetRequiredService<ISettlementReturnRepository>().AddAsync(result, ct);
            if (fallback)
            {
                await AddAttemptAsync(SettlementReturnProviderOperation.Reverse, graph.PriorRef, rejectedAttempt: true);
            }

            await AddAttemptAsync(SettlementReturnProviderOperation.Refund,
                corrupted ? graph.WalletRef : graph.PriorRef, rejected);

            async Task AddAttemptAsync(SettlementReturnProviderOperation operation, string payId, bool rejectedAttempt)
            {
                var attempt = new SettlementReturnProviderAttempt(Guid.NewGuid(), id, PaymentProvider.Csob, operation,
                    payId, at.AddMinutes(-2));
                attempt.Begin(at.AddMinutes(-1));
                if (rejectedAttempt)
                {
                    attempt.Reject("Synthetic refusal", at);
                }
                else
                {
                    attempt.Confirm(at.AddSeconds(1));
                }

                await services.GetRequiredService<ISettlementReturnProviderAttemptRepository>().AddAsync(attempt, ct);
            }
        }
    }

    private static DateTimeOffset At(int month, int day, int hour = 12, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, TimeSpan.Zero);

    private async Task<string> FinancialFingerprintAsync(ReportGraph graph)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
        return await db.Database.SqlQuery<string>($"""
            SELECT md5(jsonb_build_array(
                (SELECT jsonb_agg(to_jsonb(p) ORDER BY p.id) FROM payments.payments p WHERE p.customer_user_id = {graph.Owner}),
                (SELECT jsonb_agg(to_jsonb(i) ORDER BY i.payment_id) FROM payments.payment_initiations i
                    JOIN payments.payments p ON p.id = i.payment_id WHERE p.customer_user_id = {graph.Owner}),
                (SELECT jsonb_agg(to_jsonb(r) ORDER BY r.id) FROM payments.settlement_returns r WHERE r.customer_user_id = {graph.Owner}),
                (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id) FROM payments.settlement_return_provider_attempts a
                    JOIN payments.settlement_returns r ON r.id = a.settlement_return_id WHERE r.customer_user_id = {graph.Owner}),
                (SELECT jsonb_agg(to_jsonb(d) ORDER BY d.document_id) FROM financial_documents.documents d WHERE d.customer_user_id = {graph.Owner}),
                (SELECT jsonb_agg(to_jsonb(j) ORDER BY j.id) FROM jobs.jobs j WHERE j.customer_user_id = {graph.Owner}),
                (SELECT to_jsonb(c) FROM credits.accounts c WHERE c.id = {graph.Account}),
                (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.sequence) FROM credits.movements m WHERE m.account_id = {graph.Account}),
                (SELECT jsonb_agg(to_jsonb(pr) ORDER BY pr.id) FROM credits.print_reservations pr WHERE pr.credit_account_id = {graph.Account}),
                (SELECT jsonb_agg(to_jsonb(u) ORDER BY u.id) FROM service_units.units u WHERE u.id IN ({graph.BoundaryUnit}, {graph.HistoricalUnit})),
                (SELECT to_jsonb(m) FROM credits.manual_topup_commands m WHERE m.command_id = {graph.Manual}),
                (SELECT to_jsonb(q) FROM credits.legacy_safeq_credit_transfers q WHERE q.command_id = {graph.SafeQ})
            )::text) AS "Value"
            """).SingleAsync();
    }

    private async Task DeleteCanonicalGraphAsync(ReportGraph graph)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM payments.settlement_return_provider_attempts WHERE settlement_return_id IN
                    (SELECT id FROM payments.settlement_returns WHERE customer_user_id = {graph.Owner});
                DELETE FROM payments.settlement_returns WHERE customer_user_id = {graph.Owner};
                DELETE FROM financial_documents.documents WHERE customer_user_id = {graph.Owner};
                DELETE FROM payments.payment_initiations WHERE payment_id IN
                    (SELECT id FROM payments.payments WHERE customer_user_id = {graph.Owner});
                DELETE FROM payments.payments WHERE customer_user_id = {graph.Owner};
                DELETE FROM jobs.jobs WHERE customer_user_id = {graph.Owner};
                DELETE FROM service_units.units WHERE id IN ({graph.BoundaryUnit}, {graph.HistoricalUnit});
                DELETE FROM credits.print_reservations WHERE credit_account_id = {graph.Account};
                DELETE FROM credits.manual_topup_commands WHERE command_id = {graph.Manual};
                DELETE FROM credits.legacy_safeq_credit_transfers WHERE command_id = {graph.SafeQ};
                """);
        }

        await DeleteFixturesAsync(graph.Owner);
    }

    private static ReportWorksheet Worksheet(ZipArchive file, int number) =>
        new(Xml(file, $"xl/worksheets/sheet{number}.xml"));

    private static ReportRow FindRow(ReportWorksheet sheet, string firstCell) =>
        Assert.Single(sheet.Rows, row => row.Value("A") == firstCell);

    private sealed class ReportWorksheet(XDocument document)
    {
        public ReportRow[] Rows { get; } = document.Descendants(SheetXml + "row").Select(item => new ReportRow(item)).ToArray();

        public string Value(string address) =>
            Text(document.Descendants(SheetXml + "c").SingleOrDefault(cell => (string?)cell.Attribute("r") == address));

        public decimal Money(string address) => decimal.Parse(Value(address), CultureInfo.InvariantCulture);
    }

    private sealed class ReportRow(XElement row)
    {
        public string Value(string column) => Text(row.Elements(SheetXml + "c").SingleOrDefault(cell =>
            (string?)cell.Attribute("r") == column + (string?)row.Attribute("r")));

        public decimal Money(string column) => decimal.Parse(Value(column), CultureInfo.InvariantCulture);
    }

    private static string Text(XElement? cell) =>
        cell?.Element(SheetXml + "v")?.Value ?? string.Concat(cell?.Descendants(SheetXml + "t").Select(text => text.Value) ?? []);

    private sealed class ReportGraph(Guid owner)
    {
        public Guid Owner { get; } = owner;
        public Guid Account { get; } = Guid.NewGuid();
        public Guid PriorJob { get; } = Guid.NewGuid();
        public Guid PriorPayment { get; } = Guid.NewGuid();
        public string PriorRef => PriorPayment.ToString("N")[..15];
        public Guid BoundaryJob { get; } = Guid.NewGuid();
        public Guid BoundaryPayment { get; } = Guid.NewGuid();
        public string BoundaryRef => BoundaryPayment.ToString("N")[..15];
        public Guid BoundaryUnit { get; } = Guid.NewGuid();
        public string BoundaryUnitCode => "A" + BoundaryUnit.ToString("N")[..6].ToUpperInvariant();
        public Guid HistoricalUnit { get; } = Guid.NewGuid();
        public string HistoricalUnitCode => "F" + HistoricalUnit.ToString("N")[..6].ToUpperInvariant();
        public Guid Wallet { get; } = Guid.NewGuid();
        public string WalletRef => Wallet.ToString("N")[..15];
        public Guid CreditJob { get; } = Guid.NewGuid();
        public Guid CreditReturn { get; } = Guid.NewGuid();
        public Guid OtherReturn { get; } = Guid.NewGuid();
        public Guid GoodFallbackReturn { get; } = Guid.NewGuid();
        public Guid RejectedReturn { get; } = Guid.NewGuid();
        public Guid PrintSource { get; } = Guid.NewGuid();
        public Guid PrintDebit { get; } = Guid.NewGuid();
        public Guid Manual { get; } = Guid.NewGuid();
        public Guid SafeQ { get; } = Guid.NewGuid();
        public Guid AfterCut { get; } = Guid.NewGuid();
    }

    private sealed partial class ReportWriteObserver : DbCommandInterceptor
    {
        public int AuditWrites { get; private set; }
        public List<string> OtherWrites { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Observe(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observe(command.CommandText);
            return ValueTask.FromResult(result);
        }

        private void Observe(string command)
        {
            foreach (Match write in MutatingSql().Matches(command))
            {
                if (write.Value.Trim().Equals("INSERT INTO audit.events", StringComparison.OrdinalIgnoreCase))
                {
                    AuditWrites++;
                }
                else
                {
                    OtherWrites.Add(write.Value.Trim());
                }
            }
        }

        [GeneratedRegex(@"(?im)^\s*(?:INSERT INTO|UPDATE|DELETE FROM)\s+[\w.]+")]
        private static partial Regex MutatingSql();
    }
}
