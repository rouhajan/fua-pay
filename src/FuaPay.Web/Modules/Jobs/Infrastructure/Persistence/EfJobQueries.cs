using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;

internal sealed class EfJobQueries : IJobQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfJobQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<CustomerJobSummary> GetCustomerSummaryAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(
            customerUserId,
            nameof(customerUserId),
            "ID zákazníka nesmí být prázdné.");

        var query = CustomerVisibleJobs(customerUserId);

        var totalCount =
            await query.LongCountAsync(cancellationToken);

        var awaitingPaymentCount = await query
            .Where(
                job =>
                    job.PaymentStatus ==
                        (int)JobPaymentStatus.Unpaid &&
                    job.ProductionStatus !=
                        (int)JobProductionStatus.Cancelled)
            .LongCountAsync(cancellationToken);

        return new CustomerJobSummary(
            totalCount,
            awaitingPaymentCount);
    }

    public Task<JobDetail?> FindForCustomerAsync(
        Guid customerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(
            customerUserId,
            nameof(customerUserId),
            "ID zákazníka nesmí být prázdné.");

        ValidateId(
            jobId,
            nameof(jobId),
            "ID zakázky nesmí být prázdné.");

        return ProjectDetail(
                CustomerVisibleJobs(customerUserId)
                    .Where(job => job.Id == jobId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobPage<JobListItem>> ListForCustomerAsync(
        Guid customerUserId,
        JobListFilter filter,
        JobPageRequest page,
        CancellationToken cancellationToken = default)
    {
        ValidateId(
            customerUserId,
            nameof(customerUserId),
            "ID zákazníka nesmí být prázdné.");

        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = ApplyFilter(
            CustomerVisibleJobs(customerUserId),
            filter);

        return await CreatePageAsync(
            query,
            page,
            cancellationToken);
    }

    public async Task<ManagementJobSummary> GetManagementSummaryAsync(
        JobManagementActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var query = ManagedJobs(actor);

        var totalCount =
            await query.LongCountAsync(cancellationToken);

        var activeCount = await query
            .Where(
                job =>
                    job.ProductionStatus !=
                        (int)JobProductionStatus.Completed &&
                    job.ProductionStatus !=
                        (int)JobProductionStatus.Cancelled)
            .LongCountAsync(cancellationToken);

        var awaitingPaymentCount = await query
            .Where(
                job =>
                    job.PublishedAt != null &&
                    job.PaymentStatus ==
                        (int)JobPaymentStatus.Unpaid &&
                    job.ProductionStatus !=
                        (int)JobProductionStatus.Cancelled)
            .LongCountAsync(cancellationToken);

        return new ManagementJobSummary(
            totalCount,
            activeCount,
            awaitingPaymentCount);
    }

    public Task<JobDetail?> FindForManagementAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        ValidateId(
            jobId,
            nameof(jobId),
            "ID zakázky nesmí být prázdné.");

        return ProjectDetail(
                ManagedJobs(actor)
                    .Where(job => job.Id == jobId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobPage<JobListItem>> ListForManagementAsync(
        JobManagementActor actor,
        JobListFilter filter,
        JobPageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = ApplyFilter(
            ManagedJobs(actor),
            filter);

        return await CreatePageAsync(
            query,
            page,
            cancellationToken);
    }

    private IQueryable<JobEntity> CustomerVisibleJobs(
        Guid customerUserId)
    {
        return _dbContext.Jobs
            .AsNoTracking()
            .Where(
                job =>
                    job.CustomerUserId == customerUserId &&
                    job.PublishedAt != null);
    }

    private IQueryable<JobEntity> ManagedJobs(
        JobManagementActor actor)
    {
        var query = _dbContext.Jobs.AsNoTracking();

        if (
            actor.Scope ==
                JobManagementScope.AssignedServiceUnits
        )
        {
            query = query.Where(
                job =>
                    actor.ServiceUnitIds.Contains(
                        job.ServiceUnitId));
        }

        return query;
    }

    private static IQueryable<JobEntity> ApplyFilter(
        IQueryable<JobEntity> query,
        JobListFilter filter)
    {
        if (filter.ProductionStatus.HasValue)
        {
            var productionStatus =
                (int)filter.ProductionStatus.Value;

            query = query.Where(
                job =>
                    job.ProductionStatus == productionStatus);
        }

        if (filter.PaymentStatus.HasValue)
        {
            var paymentStatus =
                (int)filter.PaymentStatus.Value;

            query = query.Where(
                job =>
                    job.PaymentStatus == paymentStatus);
        }

        if (filter.ServiceUnitId.HasValue)
        {
            var serviceUnitId = filter.ServiceUnitId.Value;

            query = query.Where(
                job => job.ServiceUnitId == serviceUnitId);
        }

        if (filter.CustomerUserId.HasValue)
        {
            var customerUserId = filter.CustomerUserId.Value;

            query = query.Where(
                job => job.CustomerUserId == customerUserId);
        }

        if (filter.Search is not null)
        {
            var pattern = $"%{filter.Search}%";

            query = query.Where(
                job =>
                    EF.Functions.ILike(job.Number, pattern) ||
                    EF.Functions.ILike(job.Title, pattern));
        }

        if (filter.CreatedFrom.HasValue)
        {
            var createdFrom = filter.CreatedFrom.Value;
            query = query.Where(job => job.CreatedAt >= createdFrom);
        }

        if (filter.CreatedToExclusive.HasValue)
        {
            var createdToExclusive = filter.CreatedToExclusive.Value;
            query = query.Where(
                job => job.CreatedAt < createdToExclusive);
        }

        return query;
    }

    private static async Task<JobPage<JobListItem>> CreatePageAsync(
        IQueryable<JobEntity> query,
        JobPageRequest page,
        CancellationToken cancellationToken)
    {
        var totalCount =
            await query.LongCountAsync(cancellationToken);

        var items =
            await ProjectListItem(
                    query
                        .OrderByDescending(
                            job => job.CreatedAt)
                        .ThenByDescending(
                            job => job.Id)
                        .Skip(page.Offset)
                        .Take(page.Limit))
                .ToListAsync(cancellationToken);

        return new JobPage<JobListItem>(
            items,
            page.Offset,
            page.Limit,
            totalCount);
    }

    private static IQueryable<JobListItem> ProjectListItem(
        IQueryable<JobEntity> query)
    {
        return query.Select(
            job => new JobListItem(
                job.Id,
                job.Number,
                job.ServiceUnitId,
                job.CustomerUserId,
                job.CreatedByUserId,
                (ServiceType)job.ServiceType,
                job.Title,
                job.PriceMinorUnits,
                (JobProductionStatus)job.ProductionStatus,
                (JobPaymentStatus)job.PaymentStatus,
                job.CreatedAt,
                job.PublishedAt,
                job.SettledAt));
    }

    private static IQueryable<JobDetail> ProjectDetail(
        IQueryable<JobEntity> query)
    {
        return query.Select(
            job => new JobDetail(
                job.Id,
                job.Number,
                job.ServiceUnitId,
                job.CustomerUserId,
                job.CreatedByUserId,
                (ServiceType)job.ServiceType,
                job.Title,
                job.Description,
                job.PriceMinorUnits,
                (JobProductionStatus)job.ProductionStatus,
                (JobPaymentStatus)job.PaymentStatus,
                job.SettlementType.HasValue
                    ? (JobSettlementType?)job.SettlementType.Value
                    : null,
                job.SettlementReferenceId,
                job.CreatedAt,
                job.PublishedAt,
                job.SettledAt,
                job.ProductionStartedAt,
                job.ReadyForPickupAt,
                job.CompletedAt,
                job.CancelledAt,
                job.Version));
    }

    private static void ValidateId(
        Guid value,
        string parameterName,
        string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                message,
                parameterName);
        }
    }
}
