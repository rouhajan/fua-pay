using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfJobPaymentCoordination :
    IJobPaymentCoordination
{
    private static readonly int[] BlockingStatusValues =
        JobPaymentBlockingPolicy.Statuses
            .Select(status => (int)status)
            .ToArray();

    private readonly FuaPayDbContext _dbContext;

    public EfJobPaymentCoordination(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<bool> LockJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        ValidateJobId(jobId);

        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Zámek zakázky pro koordinaci úhrady vyžaduje " +
                "aktivní databázovou transakci.");
        }

        var lockedJobs = await _dbContext.Jobs
            .FromSqlInterpolated(
                $"SELECT * FROM jobs.jobs WHERE id = {jobId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return lockedJobs.Count != 0;
    }

    public Task<bool> HasBlockingDirectPaymentAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        ValidateJobId(jobId);

        return _dbContext.Payments
            .AsNoTracking()
            .AnyAsync(
                payment =>
                    payment.JobId == jobId &&
                    BlockingStatusValues.Contains(payment.Status),
                cancellationToken);
    }

    private static void ValidateJobId(Guid jobId)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }
    }
}
