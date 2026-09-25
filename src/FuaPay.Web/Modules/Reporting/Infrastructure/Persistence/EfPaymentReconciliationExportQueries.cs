using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;
using FuaPay.Web.Modules.Reporting.Application;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Reporting.Infrastructure.Persistence;

internal sealed class EfPaymentReconciliationExportQueries :
    IPaymentReconciliationExportQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfPaymentReconciliationExportQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PaymentReconciliationExportRow>> ListAsync(
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        int maximumRows,
        CancellationToken cancellationToken = default)
    {
        if (maximumRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        }

        var paymentsQuery = _dbContext.Payments
            .AsNoTracking()
            .Where(item => item.Provider == (int)PaymentProvider.Csob);
        if (from.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(
                item => item.CreatedAt >= from.Value);
        }
        if (toExclusive.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(
                item => item.CreatedAt < toExclusive.Value);
        }

        var payments = await paymentsQuery
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(maximumRows + 1)
            .ToArrayAsync(cancellationToken);
        if (payments.Length > maximumRows)
        {
            throw LimitExceeded(maximumRows);
        }

        var paymentIds = payments.Select(item => item.Id).ToArray();
        var jobIds = payments
            .Where(item => item.JobId.HasValue)
            .Select(item => item.JobId!.Value)
            .Distinct()
            .ToArray();
        var initiations = await _dbContext.PaymentInitiations
            .AsNoTracking()
            .Where(item => paymentIds.Contains(item.PaymentId))
            .ToDictionaryAsync(item => item.PaymentId, cancellationToken);
        var returns = await _dbContext.SettlementReturns
            .AsNoTracking()
            .Where(item =>
                item.OriginalPaymentId.HasValue &&
                paymentIds.Contains(item.OriginalPaymentId.Value))
            .OrderBy(item => item.RequestedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var returnIds = returns.Select(item => item.Id).ToArray();
        var attempts = await _dbContext.SettlementReturnProviderAttempts
            .AsNoTracking()
            .Where(item => returnIds.Contains(item.SettlementReturnId))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var jobs = await _dbContext.Jobs
            .AsNoTracking()
            .Where(item => jobIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var serviceUnitIds = jobs.Values
            .Select(item => item.ServiceUnitId)
            .Distinct()
            .ToArray();
        var units = await _dbContext.ServiceUnits
            .AsNoTracking()
            .Where(item => serviceUnitIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var documents = await _dbContext.FinancialDocuments
            .AsNoTracking()
            .Where(item =>
                item.SourceType == (int)FinancialDocumentSourceType.Payment &&
                paymentIds.Contains(item.SourceId))
            .ToDictionaryAsync(item => item.SourceId, cancellationToken);

        var returnsByPayment = returns
            .GroupBy(item => item.OriginalPaymentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var attemptsByReturn = attempts
            .GroupBy(item => item.SettlementReturnId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var rows = new List<PaymentReconciliationExportRow>();
        foreach (var payment in payments)
        {
            initiations.TryGetValue(payment.Id, out var initiation);
            documents.TryGetValue(payment.Id, out var document);
            var job = payment.JobId.HasValue &&
                jobs.TryGetValue(payment.JobId.Value, out var foundJob)
                ? foundJob
                : null;
            var unit = job is not null &&
                units.TryGetValue(job.ServiceUnitId, out var foundUnit)
                ? foundUnit
                : null;

            if (!returnsByPayment.TryGetValue(payment.Id, out var paymentReturns) ||
                paymentReturns.Length == 0)
            {
                rows.Add(CreateRow(
                    payment,
                    initiation?.OrderNumber,
                    document?.DocumentId,
                    document?.DocumentNumber,
                    job?.Number,
                    unit?.DisplayName,
                    settlementReturn: null,
                    attempt: null));
            }
            else
            {
                foreach (var settlementReturn in paymentReturns)
                {
                    if (!attemptsByReturn.TryGetValue(
                            settlementReturn.Id,
                            out var returnAttempts) ||
                        returnAttempts.Length == 0)
                    {
                        rows.Add(CreateRow(
                            payment,
                            initiation?.OrderNumber,
                            document?.DocumentId,
                            document?.DocumentNumber,
                            job?.Number,
                            unit?.DisplayName,
                            settlementReturn,
                            attempt: null));
                    }
                    else
                    {
                        foreach (var attempt in returnAttempts)
                        {
                            rows.Add(CreateRow(
                                payment,
                                initiation?.OrderNumber,
                                document?.DocumentId,
                                document?.DocumentNumber,
                                job?.Number,
                                unit?.DisplayName,
                                settlementReturn,
                                attempt));
                        }
                    }

                    if (rows.Count > maximumRows)
                    {
                        throw LimitExceeded(maximumRows);
                    }
                }
            }
        }

        return rows;
    }

    private static PaymentReconciliationExportRow CreateRow(
        PaymentEntity payment,
        long? orderNumber,
        Guid? documentId,
        string? documentNumber,
        string? jobNumber,
        string? serviceUnit,
        SettlementReturnEntity? settlementReturn,
        SettlementReturnProviderAttemptEntity? attempt)
    {
        return new PaymentReconciliationExportRow(
            orderNumber,
            payment.ProviderReference,
            payment.Id,
            payment.CreatedAt,
            payment.CompletedAt,
            payment.AmountMinorUnits,
            Money.CurrencyCode,
            (PaymentPurposeType)payment.PurposeType,
            jobNumber,
            serviceUnit,
            documentId,
            documentNumber,
            settlementReturn is null
                ? null
                : (SettlementReturnKind)settlementReturn.Kind,
            settlementReturn?.RequestId,
            settlementReturn?.Id,
            settlementReturn?.AmountMinorUnits,
            settlementReturn is null
                ? null
                : (SettlementReturnState)settlementReturn.State,
            attempt is null
                ? null
                : (SettlementReturnProviderOperation)attempt.Operation,
            attempt is null
                ? null
                : (SettlementReturnProviderAttemptState)attempt.State,
            settlementReturn?.RequestedAt,
            settlementReturn?.UpdatedAt,
            attempt?.StartedAt,
            attempt?.UpdatedAt,
            attempt?.FinishedAt);
    }

    private static InvalidOperationException LimitExceeded(int maximumRows) =>
        new($"Accounting reconciliation export exceeded the safe {maximumRows:N0}-row limit.");
}
