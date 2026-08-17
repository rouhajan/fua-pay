using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfPaymentQueries : IPaymentQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfPaymentQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public Task<PaymentDetail?> FindForCustomerAsync(
        Guid customerUserId,
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(customerUserId, nameof(customerUserId));
        ValidateId(paymentId, nameof(paymentId));

        return ProjectDetail(
                _dbContext.Payments
                    .AsNoTracking()
                    .Where(
                        item =>
                            item.Id == paymentId &&
                            item.CustomerUserId == customerUserId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<PaymentDetail?> FindForAdministrationAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(paymentId, nameof(paymentId));

        return ProjectDetail(
                _dbContext.Payments
                    .AsNoTracking()
                    .Where(item => item.Id == paymentId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<PaymentPage> ListForCustomerAsync(
        Guid customerUserId,
        PaymentListFilter filter,
        PaymentPageRequest page,
        CancellationToken cancellationToken = default)
    {
        ValidateId(customerUserId, nameof(customerUserId));
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = _dbContext.Payments
            .AsNoTracking()
            .Where(item => item.CustomerUserId == customerUserId);

        return CreatePageAsync(
            ApplyFilter(query, filter),
            page,
            cancellationToken);
    }

    public Task<PaymentPage> ListForAdministrationAsync(
        PaymentListFilter filter,
        PaymentPageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        return CreatePageAsync(
            ApplyFilter(
                _dbContext.Payments.AsNoTracking(),
                filter),
            page,
            cancellationToken);
    }

    private static IQueryable<PaymentEntity> ApplyFilter(
        IQueryable<PaymentEntity> query,
        PaymentListFilter filter)
    {
        if (
            filter.Status.HasValue &&
            filter.Status.Value != PaymentStatus.Unknown)
        {
            var status = (int)filter.Status.Value;
            query = query.Where(item => item.Status == status);
        }

        if (
            filter.PurposeType.HasValue &&
            filter.PurposeType.Value != PaymentPurposeType.Unknown)
        {
            var purpose = (int)filter.PurposeType.Value;
            query = query.Where(item => item.PurposeType == purpose);
        }

        if (filter.CustomerUserId.HasValue)
        {
            query = query.Where(
                item =>
                    item.CustomerUserId ==
                    filter.CustomerUserId.Value);
        }

        if (filter.CreatedFrom.HasValue)
        {
            query = query.Where(
                item => item.CreatedAt >= filter.CreatedFrom.Value);
        }

        if (filter.CreatedToExclusive.HasValue)
        {
            query = query.Where(
                item =>
                    item.CreatedAt < filter.CreatedToExclusive.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(
                item =>
                    item.ProviderReference != null &&
                    EF.Functions.ILike(
                        item.ProviderReference,
                        pattern));
        }

        return query;
    }

    private static async Task<PaymentPage> CreatePageAsync(
        IQueryable<PaymentEntity> query,
        PaymentPageRequest page,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip(page.Offset)
            .Take(page.Limit)
            .Select(item => new PaymentListItem(
                item.Id,
                item.CustomerUserId,
                (PaymentPurposeType)item.PurposeType,
                item.JobId,
                item.AmountMinorUnits,
                (PaymentProvider)item.Provider,
                (PaymentStatus)item.Status,
                item.ProviderReference,
                item.FailureReason,
                item.CreatedAt,
                item.UpdatedAt,
                item.CompletedAt))
            .ToArrayAsync(cancellationToken);

        return new PaymentPage(
            items,
            page.Offset,
            page.Limit,
            totalCount);
    }

    private IQueryable<PaymentDetail> ProjectDetail(
        IQueryable<PaymentEntity> query)
    {
        return query.Select(item => new PaymentDetail(
            item.Id,
            item.CustomerUserId,
            (PaymentPurposeType)item.PurposeType,
            item.JobId,
            item.AmountMinorUnits,
            (PaymentProvider)item.Provider,
            (PaymentStatus)item.Status,
            item.ProviderReference,
            item.FailureReason,
            item.CreatedAt,
            item.UpdatedAt,
            item.CompletedAt,
            _dbContext.PaymentInitiations
                .Where(
                    initiation =>
                        initiation.PaymentId == item.Id &&
                        initiation.State ==
                            (int)PaymentInitiationState.Initialized)
                .Select(initiation => initiation.ProcessUri)
                .SingleOrDefault(),
            item.Version));
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "ID nesmí být prázdné.",
                parameterName);
        }
    }
}
