using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PaymentReconciliationExportPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2088, 9, 25, 14, 0, 0, TimeSpan.Zero);

    private static long _orderSequence = 8_700_000_000;
    private static int _documentSequence = 100_000;

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentReconciliationExportPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListAsync_MapsAuthoritativeProvenanceAndPreservesEveryAttempt()
    {
        var scenario = await SeedAsync();

        try
        {
            using var scope = _factory.Services.CreateScope();
            var queries = scope.ServiceProvider
                .GetRequiredService<IPaymentReconciliationExportQueries>();

            var rows = await queries.ListAsync(
                TestTime.AddMinutes(-1),
                TestTime.AddMinutes(30),
                maximumRows: 20);
            var scenarioRows = rows
                .Where(item => scenario.PaymentIds.Contains(item.PaymentId))
                .ToArray();

            Assert.Equal(
                [
                    scenario.PaymentWithoutReturnId,
                    scenario.CardJobPaymentId,
                    scenario.CardJobPaymentId,
                    scenario.CardTopUpPaymentId,
                    scenario.CardTopUpPaymentId
                ],
                scenarioRows.Select(item => item.PaymentId));

            var paymentOnly = Assert.Single(
                scenarioRows,
                item => item.PaymentId == scenario.PaymentWithoutReturnId);
            Assert.Equal(scenario.PaymentOnlyOrderNumber, paymentOnly.OrderNumber);
            Assert.Equal(scenario.PaymentOnlyPayId, paymentOnly.PayId);
            Assert.Null(paymentOnly.SettlementReturnId);
            Assert.Null(paymentOnly.ProviderAttemptId);

            var cardJobRows = scenarioRows
                .Where(item => item.PaymentId == scenario.CardJobPaymentId)
                .ToArray();
            Assert.Equal(2, cardJobRows.Length);
            Assert.Equal(
                scenario.CardJobReturnIds,
                cardJobRows.Select(item => item.SettlementReturnId!.Value));
            Assert.Equal(
                scenario.CardJobAttemptIds,
                cardJobRows.Select(item => item.ProviderAttemptId!.Value));
            Assert.All(cardJobRows, item =>
            {
                Assert.Equal(scenario.CardJobOrderNumber, item.OrderNumber);
                Assert.Equal(scenario.CardJobPayId, item.PayId);
                Assert.Equal(scenario.JobNumber, item.JobNumber);
                Assert.Equal(scenario.ServiceUnitName, item.ServiceUnit);
                Assert.Equal(scenario.DocumentId, item.FinancialDocumentId);
                Assert.Equal(scenario.DocumentNumber, item.FinancialDocumentNumber);
                Assert.Equal(SettlementReturnKind.CardJob, item.ReturnKind);
                Assert.Equal(
                    SettlementReturnProviderOperation.Refund,
                    item.ProviderOperation);
            });
            Assert.Equal(
                [2_500L, 3_000L],
                cardJobRows.Select(item => item.ReturnAmountMinorUnits!.Value));

            var cardTopUpRows = scenarioRows
                .Where(item => item.PaymentId == scenario.CardTopUpPaymentId)
                .ToArray();
            Assert.Equal(2, cardTopUpRows.Length);
            Assert.All(cardTopUpRows, item =>
            {
                Assert.Equal(scenario.CardTopUpOrderNumber, item.OrderNumber);
                Assert.Equal(scenario.CardTopUpPayId, item.PayId);
                Assert.Equal(SettlementReturnKind.CardTopUp, item.ReturnKind);
                Assert.Equal(
                    scenario.CardTopUpReturnId,
                    item.SettlementReturnId);
                Assert.Null(item.JobNumber);
                Assert.Null(item.ServiceUnit);
            });
            Assert.Equal(
                scenario.CardTopUpAttemptIds,
                cardTopUpRows.Select(item => item.ProviderAttemptId!.Value));
            Assert.Equal(
                [
                    SettlementReturnProviderOperation.Reverse,
                    SettlementReturnProviderOperation.Refund
                ],
                cardTopUpRows.Select(item => item.ProviderOperation!.Value));
        }
        finally
        {
            await DeleteAsync(scenario);
        }
    }

    [Fact]
    public async Task ListAsync_AppliesLimitAfterOneToManyExpansion()
    {
        var scenario = await SeedAsync();

        try
        {
            using var scope = _factory.Services.CreateScope();
            var queries = scope.ServiceProvider
                .GetRequiredService<IPaymentReconciliationExportQueries>();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => queries.ListAsync(
                    TestTime.AddMinutes(19),
                    TestTime.AddMinutes(21),
                    maximumRows: 1));

            Assert.Contains("1-row limit", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync(scenario);
        }
    }

    private async Task<Scenario> SeedAsync()
    {
        var customerId = Guid.NewGuid();
        var administratorId = Guid.NewGuid();
        var serviceUnitId = Guid.NewGuid();
        var serviceUnitName = "Reconciliation test unit";
        var job = new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            serviceUnitId,
            customerId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Reconciliation export job",
            "Real persistence provenance test",
            new Money(12_500),
            TestTime.AddMinutes(5));
        job.Publish(TestTime.AddMinutes(6));

        var paymentOnly = CreatePayment(
            customerId,
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            amountMinorUnits: 4_000,
            TestTime,
            creationRequestId: Guid.NewGuid());
        var cardJobPayment = CreatePayment(
            customerId,
            PaymentPurposeType.Job,
            job.Id,
            job.Price.MinorUnits,
            TestTime.AddMinutes(10));
        var cardTopUpPayment = CreatePayment(
            customerId,
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            amountMinorUnits: 8_000,
            TestTime.AddMinutes(20),
            creationRequestId: Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IServiceUnitRepository>().AddAsync(
            new ServiceUnit(
                serviceUnitId,
                $"R{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
                serviceUnitName,
                ServiceType.ThreeDPrint,
                TestTime.AddMinutes(-5),
                ServiceUnitChangeActor.ForProcess("reconciliation-test")),
            CancellationToken.None);
        await services.GetRequiredService<IJobRepository>().AddAsync(
            job,
            CancellationToken.None);

        var paymentRepository = services.GetRequiredService<IPaymentRepository>();
        var paymentOnlyData = await PersistPaymentAsync(
            paymentRepository,
            paymentOnly);
        var cardJobData = await PersistPaymentAsync(
            paymentRepository,
            cardJobPayment);
        var cardTopUpData = await PersistPaymentAsync(
            paymentRepository,
            cardTopUpPayment);
        job.ConfirmSettlement(
            JobSettlementType.DirectPayment,
            cardJobPayment.Id,
            cardJobPayment.CompletedAt!.Value);
        await services.GetRequiredService<IJobRepository>().SaveAsync(
            job,
            CancellationToken.None);

        var document = FinancialDocument.CreateDirectJobCardPayment(
            Guid.NewGuid(),
            NextDocumentNumber(),
            cardJobPayment.Id,
            new FinancialDocumentCustomerSnapshot(
                customerId,
                "Persistence customer",
                "not-exported@example.test"),
            cardJobPayment.Amount.MinorUnits,
            Money.CurrencyCode,
            cardJobPayment.CompletedAt.Value,
            cardJobPayment.CompletedAt.Value.AddMinutes(1),
            TestIssuer(),
            FinancialDocumentTaxPolicy.CreateApprovedSnapshot(
                cardJobPayment.Amount.MinorUnits),
            new FinancialDocumentProviderSnapshot(
                "Csob",
                cardJobData.PayId,
                cardJobData.OrderNumber.ToString()),
            new FinancialDocumentJobSnapshot(
                job.Id,
                job.Number,
                job.Title,
                job.Description,
                serviceUnitName));
        var documentRepository = services
            .GetRequiredService<IFinancialDocumentRepository>();
        documentRepository.Stage(document);
        await documentRepository.PersistStagedAsync(document);

        var returnRepository = services
            .GetRequiredService<ISettlementReturnRepository>();
        var attemptRepository = services
            .GetRequiredService<ISettlementReturnProviderAttemptRepository>();

        var firstJobReturn = await AddCompletedReturnAsync(
            returnRepository,
            attemptRepository,
            SettlementReturnKind.CardJob,
            cardJobPayment,
            job.Id,
            administratorId,
            2_500,
            TestTime.AddMinutes(12),
            SettlementReturnProviderOperation.Refund);
        var secondJobReturn = await AddCompletedReturnAsync(
            returnRepository,
            attemptRepository,
            SettlementReturnKind.CardJob,
            cardJobPayment,
            job.Id,
            administratorId,
            3_000,
            TestTime.AddMinutes(14),
            SettlementReturnProviderOperation.Refund);

        var topUpReturn = new SettlementReturn(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SettlementReturnKind.CardTopUp,
            cardTopUpPayment.Id,
            jobId: null,
            customerId,
            administratorId,
            cardTopUpPayment.Amount,
            "Full top-up return",
            TestTime.AddMinutes(22));
        topUpReturn.Begin(TestTime.AddMinutes(23));
        topUpReturn.Complete(TestTime.AddMinutes(26));
        await returnRepository.AddAsync(topUpReturn);

        var reverseAttempt = new SettlementReturnProviderAttempt(
            topUpReturn.RequestId,
            topUpReturn.Id,
            PaymentProvider.Csob,
            SettlementReturnProviderOperation.Reverse,
            cardTopUpData.PayId,
            TestTime.AddMinutes(22));
        reverseAttempt.Begin(TestTime.AddMinutes(23));
        reverseAttempt.Reject(
            "Settled; full refund required",
            TestTime.AddMinutes(24));
        await attemptRepository.AddAsync(reverseAttempt);
        var refundAttempt = new SettlementReturnProviderAttempt(
            Guid.NewGuid(),
            topUpReturn.Id,
            PaymentProvider.Csob,
            SettlementReturnProviderOperation.Refund,
            cardTopUpData.PayId,
            TestTime.AddMinutes(24));
        refundAttempt.Begin(TestTime.AddMinutes(25));
        refundAttempt.Confirm(TestTime.AddMinutes(26));
        await attemptRepository.AddAsync(refundAttempt);

        return new Scenario(
            serviceUnitId,
            job.Id,
            job.Number,
            serviceUnitName,
            paymentOnly.Id,
            paymentOnlyData.OrderNumber,
            paymentOnlyData.PayId,
            cardJobPayment.Id,
            cardJobData.OrderNumber,
            cardJobData.PayId,
            [firstJobReturn.ReturnId, secondJobReturn.ReturnId],
            [firstJobReturn.AttemptId, secondJobReturn.AttemptId],
            document.DocumentId,
            document.DocumentNumber,
            cardTopUpPayment.Id,
            cardTopUpData.OrderNumber,
            cardTopUpData.PayId,
            topUpReturn.Id,
            [reverseAttempt.Id, refundAttempt.Id]);
    }

    private static Payment CreatePayment(
        Guid customerId,
        PaymentPurposeType purpose,
        Guid? jobId,
        long amountMinorUnits,
        DateTimeOffset createdAt,
        Guid? creationRequestId = null) =>
        new(
            Guid.NewGuid(),
            customerId,
            purpose,
            jobId,
            new Money(amountMinorUnits),
            PaymentProvider.Csob,
            createdAt,
            creationRequestId);

    private static async Task<PaymentData> PersistPaymentAsync(
        IPaymentRepository repository,
        Payment payment)
    {
        var orderNumber = Interlocked.Increment(ref _orderSequence);
        var payId = Guid.NewGuid().ToString("N")[..15];
        await repository.AddPreparedAsync(
            payment,
            new PaymentInitiation(
                payment.Id,
                PaymentProvider.Csob,
                orderNumber,
                Guid.NewGuid(),
                payment.CreatedAt));
        payment.MarkPending(payId, payment.CreatedAt.AddMinutes(1));
        payment.Complete(payment.CreatedAt.AddMinutes(2));
        await repository.SaveAsync(payment);
        return new PaymentData(orderNumber, payId);
    }

    private static async Task<ReturnData> AddCompletedReturnAsync(
        ISettlementReturnRepository returnRepository,
        ISettlementReturnProviderAttemptRepository attemptRepository,
        SettlementReturnKind kind,
        Payment payment,
        Guid? jobId,
        Guid administratorId,
        long amountMinorUnits,
        DateTimeOffset requestedAt,
        SettlementReturnProviderOperation operation)
    {
        var settlementReturn = new SettlementReturn(
            Guid.NewGuid(),
            Guid.NewGuid(),
            kind,
            payment.Id,
            jobId,
            payment.CustomerUserId,
            administratorId,
            new Money(amountMinorUnits),
            "Partial return",
            requestedAt);
        settlementReturn.Begin(requestedAt.AddMinutes(1));
        settlementReturn.Complete(requestedAt.AddMinutes(2));
        await returnRepository.AddAsync(settlementReturn);

        var attempt = new SettlementReturnProviderAttempt(
            Guid.NewGuid(),
            settlementReturn.Id,
            PaymentProvider.Csob,
            operation,
            payment.ProviderReference!,
            requestedAt);
        attempt.Begin(requestedAt.AddMinutes(1));
        attempt.Confirm(requestedAt.AddMinutes(2));
        await attemptRepository.AddAsync(attempt);
        return new ReturnData(settlementReturn.Id, attempt.Id);
    }

    private async Task DeleteAsync(Scenario scenario)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM financial_documents.documents WHERE source_id = ANY({scenario.PaymentIds})");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.settlement_return_provider_attempts
            WHERE settlement_return_id IN
            (
                SELECT id FROM payments.settlement_returns
                WHERE original_payment_id = ANY({scenario.PaymentIds})
            )
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments.settlement_returns WHERE original_payment_id = ANY({scenario.PaymentIds})");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments.payment_initiations WHERE payment_id = ANY({scenario.PaymentIds})");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM payments.payments WHERE id = ANY({scenario.PaymentIds})");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM jobs.jobs WHERE id = {scenario.JobId}");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM service_units.units WHERE id = {scenario.ServiceUnitId}");
    }

    private static string NextDocumentNumber() =>
        $"FUA-2088-{Interlocked.Increment(ref _documentSequence):D6}";

    private static FinancialDocumentIssuerSnapshot TestIssuer() =>
        new(
            "TEST ISSUER - NOT PRODUCTION",
            "TEST UNIT",
            "TEST ADDRESS 1",
            "TEST ADDRESS 2",
            "CZ",
            "TEST-ID",
            "TEST-VAT",
            "issuer@example.test");

    private sealed record PaymentData(long OrderNumber, string PayId);

    private sealed record ReturnData(Guid ReturnId, Guid AttemptId);

    private sealed record Scenario(
        Guid ServiceUnitId,
        Guid JobId,
        string JobNumber,
        string ServiceUnitName,
        Guid PaymentWithoutReturnId,
        long PaymentOnlyOrderNumber,
        string PaymentOnlyPayId,
        Guid CardJobPaymentId,
        long CardJobOrderNumber,
        string CardJobPayId,
        Guid[] CardJobReturnIds,
        Guid[] CardJobAttemptIds,
        Guid DocumentId,
        string DocumentNumber,
        Guid CardTopUpPaymentId,
        long CardTopUpOrderNumber,
        string CardTopUpPayId,
        Guid CardTopUpReturnId,
        Guid[] CardTopUpAttemptIds)
    {
        public Guid[] PaymentIds =>
            [PaymentWithoutReturnId, CardJobPaymentId, CardTopUpPaymentId];
    }
}
