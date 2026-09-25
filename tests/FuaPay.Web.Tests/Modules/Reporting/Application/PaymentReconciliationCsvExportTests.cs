using System.Reflection;
using System.Text;

using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Tests.Modules.Reporting.Application;

public sealed class PaymentReconciliationCsvExportTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Export_PreservesAttemptRowsProtectsCsvAndOmitsPersonalColumns()
    {
        var paymentId = Guid.NewGuid();
        var firstReturnId = Guid.NewGuid();
        var secondReturnId = Guid.NewGuid();
        var rows = new[]
        {
            Row(
                paymentId,
                firstReturnId,
                SettlementReturnProviderOperation.Refund,
                SettlementReturnProviderAttemptState.Confirmed,
                "=Injected unit"),
            Row(
                paymentId,
                secondReturnId,
                SettlementReturnProviderOperation.Refund,
                SettlementReturnProviderAttemptState.Uncertain,
                "Plotter")
        };
        var queries = new StubQueries(rows);
        var audit = new RecordingAuditTrail();
        var service = CreateService(queries, audit);

        var file = await service.ExportPaymentReconciliationAsync(
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));
        var csv = Encoding.UTF8.GetString(file.Content);

        Assert.StartsWith("\uFEFF", csv, StringComparison.Ordinal);
        Assert.Contains("\"orderNo\";\"payId\";\"FUA Payment ID\"", csv);
        Assert.Contains(firstReturnId.ToString(), csv);
        Assert.Contains(secondReturnId.ToString(), csv);
        Assert.Contains(rows[0].ProviderAttemptId!.Value.ToString(), csv);
        Assert.Contains("\"'=Injected unit\"", csv);
        Assert.DoesNotContain("E-mail", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Customer name", csv, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100_000, queries.MaximumRows);
        Assert.Equal(
            "export.payment-reconciliation",
            Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Export_PropagatesBoundedQueryFailureWithoutAudit()
    {
        var queries = new StubQueries(
            new InvalidOperationException("row limit"));
        var audit = new RecordingAuditTrail();
        var service = CreateService(queries, audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExportPaymentReconciliationAsync(
                Guid.NewGuid(),
                from: null,
                to: null));

        Assert.Equal(100_000, queries.MaximumRows);
        Assert.Empty(audit.Entries);
    }

    private static AdministrationCsvExportService CreateService(
        IPaymentReconciliationExportQueries queries,
        IAuditTrail audit) =>
        new(
            Proxy<IJobQueries>(),
            Proxy<ICreditQueries>(),
            Proxy<IPaymentQueries>(),
            Proxy<IAccessUserQueries>(),
            Proxy<IServiceUnitQueries>(),
            new FixedTimeProvider(Now),
            audit,
            queries);

    private static PaymentReconciliationExportRow Row(
        Guid paymentId,
        Guid returnId,
        SettlementReturnProviderOperation operation,
        SettlementReturnProviderAttemptState attemptState,
        string serviceUnit) =>
        new(
            123456,
            "pay123",
            paymentId,
            Now.AddDays(-2),
            Now.AddDays(-2).AddMinutes(1),
            12_500,
            "CZK",
            PaymentPurposeType.Job,
            "PLT-2026-000001",
            serviceUnit,
            Guid.NewGuid(),
            "FUA-2026-000001",
            SettlementReturnKind.CardJob,
            Guid.NewGuid(),
            returnId,
            2_500,
            SettlementReturnState.Completed,
            Guid.NewGuid(),
            operation,
            attemptState,
            Now.AddDays(-1),
            Now,
            Now.AddDays(-1),
            Now,
            Now);

    private static T Proxy<T>() where T : class =>
        DispatchProxy.Create<T, ThrowingProxy>();

    private class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            throw new NotSupportedException(
                targetMethod?.Name ?? "Unexpected proxy call");
    }

    private sealed class StubQueries : IPaymentReconciliationExportQueries
    {
        private readonly IReadOnlyList<PaymentReconciliationExportRow>? _rows;
        private readonly Exception? _exception;

        public StubQueries(IReadOnlyList<PaymentReconciliationExportRow> rows) =>
            _rows = rows;

        public StubQueries(Exception exception) => _exception = exception;

        public int MaximumRows { get; private set; }

        public Task<IReadOnlyList<PaymentReconciliationExportRow>> ListAsync(
            DateTimeOffset? from,
            DateTimeOffset? toExclusive,
            int maximumRows,
            CancellationToken cancellationToken = default)
        {
            MaximumRows = maximumRows;
            return _exception is null
                ? Task.FromResult(_rows!)
                : Task.FromException<IReadOnlyList<
                    PaymentReconciliationExportRow>>(_exception);
        }
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
