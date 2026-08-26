using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class PrintReservationService
{
    private readonly ICreditAccountRepository _creditAccountRepository;
    private readonly IPrintReservationRepository _reservationRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;

    public PrintReservationService(
        ICreditAccountRepository creditAccountRepository,
        IPrintReservationRepository reservationRepository,
        IApplicationTransaction transaction,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(creditAccountRepository);
        ArgumentNullException.ThrowIfNull(reservationRepository);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _creditAccountRepository = creditAccountRepository;
        _reservationRepository = reservationRepository;
        _transaction = transaction;
        _timeProvider = timeProvider;
    }

    public async Task<PrintReservationResult> ReserveAsync(
        ReservePrintCreditCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return await _transaction.ExecuteAsync(
                ct => ReserveInsideTransactionAsync(command, ct),
                cancellationToken);
        }
        catch (PrintReservationReserveCommandAlreadyExistsException exception)
        {
            return await ResolveConcurrentUniqueConflictAsync(
                command,
                exception,
                cancellationToken);
        }
        catch (PrintReservationPrintJobAlreadyExistsException exception)
        {
            return await ResolveConcurrentUniqueConflictAsync(
                command,
                exception,
                cancellationToken);
        }
    }

    private async Task<PrintReservationResult> ReserveInsideTransactionAsync(
        ReservePrintCreditCommand command,
        CancellationToken cancellationToken)
    {
        var account = await FindAndLockAccountAsync(
            command.OwnerId,
            cancellationToken);

        var existingCommand =
            await _reservationRepository.FindByReserveCommandAsync(
                command.PrintSourceId,
                command.ReserveCommandId,
                cancellationToken);

        if (existingCommand is not null)
        {
            return ResolveCommandReplay(
                command,
                account.Id,
                existingCommand);
        }

        var existingJob = await _reservationRepository.FindByPrintJobAsync(
            command.PrintSourceId,
            command.JobUuid,
            cancellationToken);

        if (existingJob is not null)
        {
            throw new PrintReservationJobConflictException(
                command.PrintSourceId,
                command.JobUuid);
        }

        var blockingAmount =
            await _reservationRepository.GetBlockingAmountAsync(
                account.Id,
                cancellationToken);
        var available = account.Balance.Subtract(blockingAmount);

        if (command.Amount.MinorUnits > available.MinorUnits)
        {
            throw new InsufficientAvailablePrintCreditException(
                command.OwnerId,
                command.Amount,
                available);
        }

        var reservation = new PrintReservation(
            Guid.NewGuid(),
            account.Id,
            command.PrintSourceId,
            command.JobUuid,
            command.Amount,
            command.ReserveCommandId,
            _timeProvider.GetUtcNow());

        await _reservationRepository.AddAsync(
            reservation,
            cancellationToken);

        return ToResult(reservation);
    }

    private async Task<PrintReservationResult>
        ResolveConcurrentUniqueConflictAsync(
            ReservePrintCreditCommand command,
            InvalidOperationException uniqueException,
            CancellationToken cancellationToken)
    {
        return await _transaction.ExecuteAsync(
            async ct =>
            {
                var account = await FindAndLockAccountAsync(
                    command.OwnerId,
                    ct);

                var existingCommand =
                    await _reservationRepository.FindByReserveCommandAsync(
                        command.PrintSourceId,
                        command.ReserveCommandId,
                        ct);

                if (existingCommand is not null)
                {
                    return ResolveCommandReplay(
                        command,
                        account.Id,
                        existingCommand);
                }

                var existingJob =
                    await _reservationRepository.FindByPrintJobAsync(
                        command.PrintSourceId,
                        command.JobUuid,
                        ct);

                if (existingJob is not null)
                {
                    throw new PrintReservationJobConflictException(
                        command.PrintSourceId,
                        command.JobUuid);
                }

                throw uniqueException;
            },
            cancellationToken);
    }

    private async Task<CreditAccount> FindAndLockAccountAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        return await _creditAccountRepository
            .FindByOwnerIdForUpdateAsync(
                ownerId,
                cancellationToken)
            ?? throw new CreditAccountNotFoundException(ownerId);
    }

    private static PrintReservationResult ResolveCommandReplay(
        ReservePrintCreditCommand command,
        Guid creditAccountId,
        PrintReservationResult existing)
    {
        if (
            existing.CreditAccountId != creditAccountId ||
            existing.PrintSourceId != command.PrintSourceId ||
            existing.ReserveCommandId != command.ReserveCommandId ||
            !string.Equals(
                existing.JobUuid,
                command.JobUuid,
                StringComparison.Ordinal) ||
            existing.Amount != command.Amount)
        {
            throw new PrintReservationCommandConflictException(
                command.PrintSourceId,
                command.ReserveCommandId);
        }

        return existing;
    }

    private static PrintReservationResult ToResult(
        PrintReservation reservation)
    {
        return new PrintReservationResult(
            reservation.Id,
            reservation.CreditAccountId,
            reservation.PrintSourceId,
            reservation.JobUuid,
            reservation.Amount,
            reservation.Status,
            reservation.ReserveCommandId,
            reservation.ResolutionCommandId,
            reservation.TerminalCommandId,
            reservation.DebitOperationId,
            reservation.CreatedAt,
            reservation.StateChangedAt,
            Version: 1);
    }
}
