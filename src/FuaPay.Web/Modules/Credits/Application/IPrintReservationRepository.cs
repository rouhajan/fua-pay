using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public interface IPrintReservationRepository
{
    Task<PrintReservationResult?> FindByReserveCommandAsync(
        Guid printSourceId,
        Guid reserveCommandId,
        CancellationToken cancellationToken);

    Task<PrintReservationResult?> FindByPrintJobAsync(
        Guid printSourceId,
        string jobUuid,
        CancellationToken cancellationToken);

    Task<Money> GetBlockingAmountAsync(
        Guid creditAccountId,
        CancellationToken cancellationToken);

    Task AddAsync(
        PrintReservation reservation,
        CancellationToken cancellationToken);
}
