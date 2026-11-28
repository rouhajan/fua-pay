using System.Data;

using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Reporting.Infrastructure.Persistence;

internal sealed class EfAccountingWorkbookQueries(FuaPayDbContext db)
{
    private const int SourceLimit = 10_000;
    private const int HistoryLimit = 100_000;

    public async Task<AccountingSnapshot> ReadAsync(AccountingPeriod period, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var token = budget.Token;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '10s'", token);

        // First snapshot-taking statement. Later reads in this transaction see the same committed data.
        var capturedAt = await db.Database.SqlQuery<DateTimeOffset>(
            $"""SELECT statement_timestamp() AS "Value" """).SingleAsync(token);
        var from = period.From;
        var cutoff = capturedAt < period.Until ? capturedAt : period.Until;
        if (cutoff <= from)
        {
            throw new AccountingWorkbookFuturePeriodException();
        }

        var rawCard = await db.Payments.AsNoTracking().Where(item =>
            item.Provider == (int)PaymentProvider.Csob && item.Status == (int)PaymentStatus.Succeeded &&
            item.CompletedAt >= from && item.CompletedAt < cutoff)
            .GroupBy(_ => 1).Select(rows => new { Count = rows.Count(), Amount = rows.Sum(item => item.AmountMinorUnits) })
            .SingleOrDefaultAsync(token);
        var rawReturns = await db.SettlementReturns.AsNoTracking().Where(item =>
            item.State == (int)SettlementReturnState.Completed &&
            (item.Kind == (int)SettlementReturnKind.CardTopUp || item.Kind == (int)SettlementReturnKind.CardJob) &&
            item.CompletedAt >= from && item.CompletedAt < cutoff)
            .GroupBy(_ => 1).Select(rows => new { Count = rows.Count(), Amount = rows.Sum(item => item.AmountMinorUnits) })
            .SingleOrDefaultAsync(token);
        var raw = new AccountingRawTotals(rawCard?.Count ?? 0, rawCard?.Amount ?? 0,
            rawReturns?.Count ?? 0, rawReturns?.Amount ?? 0);

        var accounts = await ReadAsync(db.CreditAccounts, token);
        var movements = await ReadAsync(db.CreditMovements, token, HistoryLimit);
        var movementOps = movements.Where(item => item.RecordedAt >= from && item.RecordedAt < cutoff)
            .Select(item => item.OperationId).Distinct().ToArray();
        if (movementOps.Length > SourceLimit)
        {
            throw new AccountingWorkbookLimitException();
        }

        var jobsInPeriod = db.Jobs.Where(job => (job.CreatedAt >= from && job.CreatedAt < cutoff) ||
            (job.SettledAt >= from && job.SettledAt < cutoff));
        var jobIds = (await ReadAsync(jobsInPeriod.Select(job => new { job.Id }), token))
            .Select(job => job.Id).ToArray();
        var relevantReturns = db.SettlementReturns.Where(item =>
            (item.CompletedAt >= from && item.CompletedAt < cutoff) ||
            (item.RequestedAt >= from && item.RequestedAt < cutoff) ||
            (item.JobId.HasValue && jobIds.Contains(item.JobId.Value) && item.CompletedAt < cutoff) ||
            movementOps.Contains(item.Id));
        var returnedPayments = relevantReturns.Where(item => item.OriginalPaymentId.HasValue)
            .Select(item => item.OriginalPaymentId!.Value);
        var returns = await ReadAsync(db.SettlementReturns.Where(item =>
            relevantReturns.Select(row => row.Id).Contains(item.Id) ||
            (item.OriginalPaymentId.HasValue && returnedPayments.Contains(item.OriginalPaymentId.Value) &&
             item.State == (int)SettlementReturnState.Completed && item.CompletedAt < cutoff)), token);
        var returnIds = returns.Select(item => item.Id).ToArray();
        var returnedPaymentIds = returns.Where(item => item.OriginalPaymentId.HasValue)
            .Select(item => item.OriginalPaymentId!.Value).Distinct().ToArray();
        var returnedJobs = returns.Where(item => item.JobId.HasValue)
            .Select(item => item.JobId!.Value).Distinct().ToArray();
        var payments = await ReadAsync(db.Payments.Where(item =>
            (item.Provider == (int)PaymentProvider.Csob &&
                ((item.CreatedAt >= from && item.CreatedAt < cutoff) ||
                 (item.CompletedAt >= from && item.CompletedAt < cutoff))) ||
            returnedPaymentIds.Contains(item.Id) || movementOps.Contains(item.Id) ||
            (item.JobId.HasValue && (jobIds.Contains(item.JobId.Value) || returnedJobs.Contains(item.JobId.Value)))), token);
        var relatedJobs = payments.Where(item => item.JobId.HasValue)
            .Select(item => item.JobId!.Value).Concat(returnedJobs).Concat(jobIds).Concat(movementOps).Distinct().ToArray();
        var jobs = await ReadAsync(db.Jobs.Where(item => relatedJobs.Contains(item.Id)), token);
        var unitIds = jobs.Select(item => item.ServiceUnitId).Distinct().ToArray();
        var units = await ReadAsync(db.ServiceUnits.Where(item => item.Status == (int)ServiceUnitStatus.Active || unitIds.Contains(item.Id)), token);
        var paymentIds = payments.Select(item => item.Id).ToArray();
        var initiations = await ReadAsync(db.PaymentInitiations.Where(item => paymentIds.Contains(item.PaymentId))
            .Select(item => new AccountingInitiation(item.PaymentId, item.Provider, item.State, item.OrderNumber)), token);
        var recoveries = await ReadAsync(db.CsobPaymentRecoveries.Where(item => paymentIds.Contains(item.PaymentId))
            .Select(item => new AccountingRecovery(item.PaymentId, item.State)), token);
        var attempts = await ReadAsync(db.SettlementReturnProviderAttempts
            .Where(item => returnIds.Contains(item.SettlementReturnId)), token);
        var manualTopUps = await ReadAsync(db.ManualCreditTopUpCommands.Where(item => movementOps.Contains(item.CommandId) ||
            (item.AcceptedAt >= from && item.AcceptedAt < cutoff)), token);
        var manualIds = manualTopUps.Select(item => item.CommandId).ToArray();
        var documents = await ReadAsync(db.FinancialDocuments.Where(item =>
            (item.SourceType == (int)FinancialDocumentSourceType.Payment && paymentIds.Contains(item.SourceId)) ||
            (item.SourceType == (int)FinancialDocumentSourceType.ManualCreditTopUp && manualIds.Contains(item.SourceId))), token);
        var safeQ = await ReadAsync(db.LegacySafeQCreditTransfers.Where(item => movementOps.Contains(item.CommandId)), token);
        var adjustments = await ReadAsync(db.CreditAdjustmentCommands.Where(item => movementOps.Contains(item.CommandId)), token);
        var prints = await ReadAsync(db.PrintReservations.Where(item => item.Status == (int)PrintReservationStatus.Captured &&
            ((item.StateChangedAt >= from && item.StateChangedAt < cutoff) ||
             (item.DebitOperationId.HasValue && movementOps.Contains(item.DebitOperationId.Value)))), token);
        var userIds = accounts.Select(item => item.OwnerId).Concat(jobs.Select(item => item.CustomerUserId))
            .Distinct().ToArray();
        var users = await ReadAsync(db.AccessUsers.Where(item => userIds.Contains(item.Id)), token);
        await transaction.CommitAsync(token);

        return new(period, capturedAt, cutoff, raw, accounts, movements, payments, initiations, recoveries,
            returns, attempts, jobs, units, documents, manualTopUps, safeQ, adjustments, prints, users);
    }

    private static async Task<T[]> ReadAsync<T>(IQueryable<T> query, CancellationToken cancellationToken,
        int limit = SourceLimit) where T : class
    {
        var rows = await query.AsNoTracking().Take(limit + 1).ToArrayAsync(cancellationToken);
        return rows.Length <= limit ? rows : throw new AccountingWorkbookLimitException();
    }
}
