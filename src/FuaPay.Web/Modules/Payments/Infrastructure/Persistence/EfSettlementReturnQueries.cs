using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfSettlementReturnQueries : ISettlementReturnQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfSettlementReturnQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<
        IReadOnlyDictionary<Guid, SettlementReturnAdministrationItem>>
        FindByOriginalPaymentIdsAsync(
            IEnumerable<Guid> originalPaymentIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalPaymentIds);

        var paymentIds = originalPaymentIds
            .Distinct()
            .ToArray();

        if (paymentIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "Original payment IDs must not contain an empty ID.",
                nameof(originalPaymentIds));
        }

        if (paymentIds.Length == 0)
        {
            return new Dictionary<
                Guid,
                SettlementReturnAdministrationItem>();
        }

        var returns = await _dbContext.SettlementReturns
            .AsNoTracking()
            .Where(item =>
                item.OriginalPaymentId.HasValue &&
                paymentIds.Contains(item.OriginalPaymentId.Value))
            .Select(item => new
            {
                item.Id,
                item.RequestId,
                item.Kind,
                OriginalPaymentId = item.OriginalPaymentId!.Value,
                item.State,
                item.Reason
            })
            .ToArrayAsync(cancellationToken);

        var returnIds = returns
            .Select(item => item.Id)
            .ToArray();
        var providerAttempts = await _dbContext
            .SettlementReturnProviderAttempts
            .AsNoTracking()
            .Where(item => returnIds.Contains(item.SettlementReturnId))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.SettlementReturnId,
                item.Provider,
                item.Operation,
                item.State,
                item.Diagnostic
            })
            .ToArrayAsync(cancellationToken);
        var providerAttemptsByReturn = providerAttempts
            .GroupBy(item => item.SettlementReturnId)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(item =>
                    item.State is
                        (int)SettlementReturnProviderAttemptState.Prepared or
                        (int)SettlementReturnProviderAttemptState.InProgress or
                        (int)SettlementReturnProviderAttemptState.Confirmed or
                        (int)SettlementReturnProviderAttemptState.Uncertain)
                    ?? group.First());

        return returns.ToDictionary(
            item => item.OriginalPaymentId,
            item =>
            {
                var attempt = providerAttemptsByReturn.GetValueOrDefault(
                    item.Id);
                return new SettlementReturnAdministrationItem(
                    item.Id,
                    item.RequestId,
                    (SettlementReturnKind)item.Kind,
                    item.OriginalPaymentId,
                    (SettlementReturnState)item.State,
                    item.Reason,
                    attempt?.Id,
                    (PaymentProvider?)attempt?.Provider,
                    (SettlementReturnProviderOperation?)attempt?.Operation,
                    (SettlementReturnProviderAttemptState?)attempt?.State,
                    attempt?.Diagnostic);
            });
    }
}
