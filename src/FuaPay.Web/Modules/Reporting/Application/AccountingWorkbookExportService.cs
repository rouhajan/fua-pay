using System.Diagnostics;

using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Reporting.Infrastructure.Persistence;
using FuaPay.Web.Modules.Reporting.Infrastructure.Xlsx;

namespace FuaPay.Web.Modules.Reporting.Application;

public sealed partial class AccountingWorkbookExportService(
    FuaPayDbContext db, IAuditTrail audit, TimeProvider clock, IConfiguration configuration,
    ILogger<AccountingWorkbookExportService> logger)
{
    private static readonly SemaphoreSlim Slot = new(1, 1);

    internal async Task<AccountingWorkbookDownload> PrepareAsync(
        AccountingPeriod period, Guid administrator, CancellationToken cancellationToken)
    {
        if (administrator == Guid.Empty)
        {
            throw new ArgumentException("Missing administrator.", nameof(administrator));
        }

        if (!await Slot.WaitAsync(0, cancellationToken))
        {
            throw new AccountingWorkbookBusyException();
        }

        var started = Stopwatch.GetTimestamp();
        var output = new BoundedWorkbookStream();
        var transferred = false;
        try
        {
            var configured = configuration["AccountingWorkbook:ClassroomPrintSourceId"];
            Guid? classroom = string.IsNullOrEmpty(configured) ? null : Guid.Parse(configured);
            if (classroom == Guid.Empty)
            {
                throw new InvalidOperationException("Invalid accounting classroom source.");
            }

            // The PostgreSQL read-only snapshot is fully disposed before rendering or audit.
            var snapshot = await new EfAccountingWorkbookQueries(db).ReadAsync(period, cancellationToken);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(30));
            var report = new AccountingReportBuilder(snapshot, classroom).Build();
            new AccountingWorkbookWriter(report, budget.Token).Write(output);
            budget.Token.ThrowIfCancellationRequested();
            output.Position = 0;

            if (db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges())
            {
                throw new InvalidOperationException("Accounting export unexpectedly has pending changes.");
            }

            var run = Guid.NewGuid();
            var description = FormattableString.Invariant(
                $"XLSX v1; period {period.Start:yyyy-MM-dd}..{period.End:yyyy-MM-dd}; captured {report.CapturedAt:O}; cutoff {report.Cutoff:O}; card {report.Card.Length}; movements {report.Movements.Length}; balances {report.Balances.Length}; jobs {report.Jobs.Length}; centre events {report.Centres.Length}; issues {report.Problems.Length}.");
            await audit.WriteAsync(AuditEntry.ForUser(administrator, "export.accounting-workbook",
                "export", run.ToString("D"), description, clock.GetUtcNow()), budget.Token);
            LogPrepared(logger, output.Length, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                report.Card.Length, report.Movements.Length, report.Problems.Length);

            var download = new AccountingWorkbookDownload(output, FormattableString.Invariant(
                $"fua-pay-ucetni-{period.Start:yyyy-MM-dd}--{period.End:yyyy-MM-dd}.xlsx"), Slot);
            transferred = true;
            return download;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, exception.GetType().Name);
            throw;
        }
        finally
        {
            if (!transferred)
            {
                output.Dispose();
                Slot.Release();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Accounting workbook prepared. Bytes: {Bytes}, ms: {Elapsed}, card: {Card}, movements: {Movements}, issues: {Issues}.")]
    private static partial void LogPrepared(ILogger logger, long bytes, double elapsed, int card, int movements, int issues);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Accounting workbook failed. Failure type: {FailureType}.")]
    private static partial void LogFailed(ILogger logger, string failureType);
}

internal sealed class AccountingWorkbookBusyException : Exception;

internal sealed class AccountingWorkbookDownload(Stream content, string name, SemaphoreSlim slot) : IDisposable
{
    private int _disposed;
    public Stream Content { get; } = content;
    public string Name { get; } = name;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                Content.Dispose();
            }
            finally
            {
                slot.Release();
            }
        }
    }
}

internal sealed class BoundedWorkbookStream : MemoryStream
{
    internal const long Limit = 16L * 1024 * 1024;

    public override void Write(byte[] buffer, int offset, int count)
    {
        Check(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Check(buffer.Length);
        base.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        Check(1);
        base.WriteByte(value);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Check(count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Check(buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    public override void SetLength(long value)
    {
        if (value > Limit)
        {
            throw new AccountingWorkbookLimitException();
        }

        base.SetLength(value);
    }

    private void Check(long count)
    {
        if (count > Limit - Position)
        {
            throw new AccountingWorkbookLimitException();
        }
    }
}
