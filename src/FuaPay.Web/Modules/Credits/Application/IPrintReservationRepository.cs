using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public interface IPrintReservationRepository
{
    Task<PrintReservationResult?> FindByIdAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<PrintReservation?> FindByIdForUpdateAsync(
        Guid reservationId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<PrintReservationResult?> FindByReserveCommandAsync(
        Guid printSourceId,
        Guid reserveCommandId,
        CancellationToken cancellationToken);

    Task<PrintReservationResult?> FindByPrintJobAsync(
        Guid printSourceId,
        string jobUuid,
        CancellationToken cancellationToken);

    Task<PrintReservationResult?> FindByResolutionCommandAsync(
        Guid printSourceId,
        Guid resolutionCommandId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<PrintReservationResult?> FindByTerminalCommandAsync(
        Guid printSourceId,
        Guid terminalCommandId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task AddAsync(
        PrintReservation reservation,
        CancellationToken cancellationToken);

    Task SaveAsync(
        PrintReservation reservation,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
