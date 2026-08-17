using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class EfAccessAdministrationLock :
    IAccessAdministrationLock
{
    private const long AdvisoryLockKey = 46_822_941_001L;

    private readonly FuaPayDbContext _dbContext;

    public EfAccessAdministrationLock(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task AcquireAsync(
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Zámek administrace přístupů vyžaduje aktivní databázovou transakci.");
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockKey})",
            cancellationToken);
    }
}
