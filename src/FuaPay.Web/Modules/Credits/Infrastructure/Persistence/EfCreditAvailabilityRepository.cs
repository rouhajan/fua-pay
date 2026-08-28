using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfCreditAvailabilityRepository :
    ICreditAvailabilityRepository
{
    private static readonly int[] BlockingPrintStatuses =
    [
        (int)PrintReservationStatus.Reserved,
        (int)PrintReservationStatus.ResolutionRequired
    ];

    private readonly FuaPayDbContext _dbContext;

    public EfCreditAvailabilityRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<Money> GetTotalBlockingAmountAsync(
        Guid creditAccountId,
        CancellationToken cancellationToken = default)
    {
        if (creditAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Credit account ID must not be empty.",
                nameof(creditAccountId));
        }

        var printBlocking = await _dbContext.PrintReservations
            .AsNoTracking()
            .Where(
                reservation =>
                    reservation.CreditAccountId == creditAccountId &&
                    BlockingPrintStatuses.Contains(reservation.Status))
            .SumAsync(
                reservation => reservation.AmountMinorUnits,
                cancellationToken);

        var returnBlocking = await _dbContext.CreditReturnHolds
            .AsNoTracking()
            .Where(
                hold =>
                    hold.CreditAccountId == creditAccountId &&
                    hold.State == (int)CreditReturnHoldState.Active)
            .SumAsync(
                hold => hold.AmountMinorUnits,
                cancellationToken);

        return new Money(checked(printBlocking + returnBlocking));
    }
}
