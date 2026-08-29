using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class PrintReservationService
{
    private const string AuditActorProcessName =
        "fua-print-payments";

    private readonly ICreditAccountRepository _creditAccountRepository;
    private readonly IPrintReservationRepository _reservationRepository;
    private readonly CreditAvailabilityService _availabilityService;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;

    public PrintReservationService(
        ICreditAccountRepository creditAccountRepository,
        IPrintReservationRepository reservationRepository,
        CreditAvailabilityService availabilityService,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(creditAccountRepository);
        ArgumentNullException.ThrowIfNull(reservationRepository);
        ArgumentNullException.ThrowIfNull(availabilityService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _creditAccountRepository = creditAccountRepository;
        _reservationRepository = reservationRepository;
        _availabilityService = availabilityService;
        _transaction = transaction;
        _auditTrail = auditTrail;
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

    public Task<PrintReservationResult?> FindByPrintJobAsync(
        Guid printSourceId,
        string jobUuid,
        CancellationToken cancellationToken = default)
    {
        PrintReservationCommandValidation.ValidateId(
            printSourceId,
            nameof(printSourceId));

        return _reservationRepository.FindByPrintJobAsync(
            printSourceId,
            IppJobUuid.Normalize(jobUuid),
            cancellationToken);
    }

    public async Task<PrintReservationResult> RequireResolutionAsync(
        RequirePrintReservationResolutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return await _transaction.ExecuteAsync(
                ct => RequireResolutionInsideTransactionAsync(
                    command,
                    ct),
                cancellationToken);
        }
        catch (PrintReservationResolutionCommandAlreadyExistsException)
        {
            return await _transaction.ExecuteAsync(
                ct => RequireResolutionInsideTransactionAsync(
                    command,
                    ct),
                cancellationToken);
        }
    }

    public async Task<PrintReservationResult> CaptureAsync(
        CapturePrintReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return await _transaction.ExecuteAsync(
                ct => CaptureInsideTransactionAsync(command, ct),
                cancellationToken);
        }
        catch (PrintReservationTerminalCommandAlreadyExistsException)
        {
            return await _transaction.ExecuteAsync(
                ct => CaptureInsideTransactionAsync(command, ct),
                cancellationToken);
        }
    }

    public async Task<PrintReservationResult> ReleaseAsync(
        ReleasePrintReservationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return await _transaction.ExecuteAsync(
                ct => ReleaseInsideTransactionAsync(command, ct),
                cancellationToken);
        }
        catch (PrintReservationTerminalCommandAlreadyExistsException)
        {
            return await _transaction.ExecuteAsync(
                ct => ReleaseInsideTransactionAsync(command, ct),
                cancellationToken);
        }
    }

    private async Task<PrintReservationResult>
        RequireResolutionInsideTransactionAsync(
            RequirePrintReservationResolutionCommand command,
            CancellationToken cancellationToken)
    {
        var locked = await FindAndLockReservationAsync(
            command.ReservationId,
            command.PrintSourceId,
            cancellationToken);
        var existingCommand =
            await _reservationRepository.FindByResolutionCommandAsync(
                command.PrintSourceId,
                command.ResolutionCommandId,
                cancellationToken);

        if (existingCommand is not null)
        {
            if (
                existingCommand.Id == locked.Reservation.Id &&
                existingCommand.ResolutionCommandId ==
                    command.ResolutionCommandId)
            {
                return existingCommand;
            }

            throw new PrintReservationResolutionCommandConflictException(
                command.PrintSourceId,
                command.ResolutionCommandId);
        }

        if (locked.Reservation.Status != PrintReservationStatus.Reserved)
        {
            throw new PrintReservationResolutionCommandConflictException(
                command.PrintSourceId,
                command.ResolutionCommandId);
        }

        var changedAt = _timeProvider.GetUtcNow();
        _ = locked.Reservation.RequireResolution(
            command.ResolutionCommandId,
            changedAt);
        StageResolutionAudit(locked.Reservation, changedAt);

        await _reservationRepository.SaveAsync(
            locked.Reservation,
            cancellationToken);

        return await ReadPersistedResultAsync(
            locked.Reservation.Id,
            cancellationToken);
    }

    private async Task<PrintReservationResult>
        CaptureInsideTransactionAsync(
            CapturePrintReservationCommand command,
            CancellationToken cancellationToken)
    {
        var locked = await FindAndLockReservationAsync(
            command.ReservationId,
            command.PrintSourceId,
            cancellationToken);
        var replay = await ResolveTerminalCommandAsync(
            locked.Reservation,
            command.PrintSourceId,
            command.TerminalCommandId,
            PrintReservationStatus.Captured,
            cancellationToken);

        if (replay is not null)
        {
            return replay;
        }

        var spendableBalance = await _availabilityService
            .GetAvailableExcludingAsync(
                locked.Account,
                locked.Reservation.Amount,
                cancellationToken);
        var debitOperationId = Guid.NewGuid();
        var changedAt = _timeProvider.GetUtcNow();

        _ = locked.Account.Debit(
            debitOperationId,
            locked.Reservation.Amount,
            spendableBalance,
            changedAt,
            $"Capture print reservation {locked.Reservation.Id}");
        _ = locked.Reservation.Capture(
            command.TerminalCommandId,
            debitOperationId,
            changedAt);
        StageCaptureAudit(locked.Reservation, changedAt);

        await _creditAccountRepository.SaveAsync(
            locked.Account,
            cancellationToken);
        await _reservationRepository.SaveAsync(
            locked.Reservation,
            cancellationToken);

        return await ReadPersistedResultAsync(
            locked.Reservation.Id,
            cancellationToken);
    }

    private async Task<PrintReservationResult>
        ReleaseInsideTransactionAsync(
            ReleasePrintReservationCommand command,
            CancellationToken cancellationToken)
    {
        var locked = await FindAndLockReservationAsync(
            command.ReservationId,
            command.PrintSourceId,
            cancellationToken);
        var replay = await ResolveTerminalCommandAsync(
            locked.Reservation,
            command.PrintSourceId,
            command.TerminalCommandId,
            PrintReservationStatus.Released,
            cancellationToken);

        if (replay is not null)
        {
            return replay;
        }

        var changedAt = _timeProvider.GetUtcNow();
        _ = locked.Reservation.Release(
            command.TerminalCommandId,
            changedAt);
        StageReleaseAudit(locked.Reservation, changedAt);

        await _reservationRepository.SaveAsync(
            locked.Reservation,
            cancellationToken);

        return await ReadPersistedResultAsync(
            locked.Reservation.Id,
            cancellationToken);
    }

    private async Task<LockedReservation> FindAndLockReservationAsync(
        Guid reservationId,
        Guid printSourceId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _reservationRepository.FindByIdAsync(
            reservationId,
            cancellationToken)
            ?? throw new PrintReservationNotFoundException(reservationId);
        var account = await _creditAccountRepository.FindByIdForUpdateAsync(
            snapshot.CreditAccountId,
            cancellationToken)
            ?? throw new InvalidDataException(
                $"Credit account '{snapshot.CreditAccountId}' for print reservation '{reservationId}' was not found.");
        var reservation =
            await _reservationRepository.FindByIdForUpdateAsync(
                reservationId,
                cancellationToken)
            ?? throw new PrintReservationNotFoundException(reservationId);

        if (reservation.CreditAccountId != account.Id)
        {
            throw new InvalidDataException(
                $"Print reservation '{reservationId}' changed its credit account identity.");
        }

        if (reservation.PrintSourceId != printSourceId)
        {
            throw new PrintReservationSourceConflictException(
                reservationId,
                printSourceId);
        }

        return new LockedReservation(account, reservation);
    }

    private async Task<PrintReservationResult?> ResolveTerminalCommandAsync(
        PrintReservation reservation,
        Guid printSourceId,
        Guid terminalCommandId,
        PrintReservationStatus requestedStatus,
        CancellationToken cancellationToken)
    {
        var existingCommand =
            await _reservationRepository.FindByTerminalCommandAsync(
                printSourceId,
                terminalCommandId,
                cancellationToken);

        if (existingCommand is not null)
        {
            if (
                existingCommand.Id == reservation.Id &&
                existingCommand.Status == requestedStatus)
            {
                return existingCommand;
            }

            throw new PrintReservationTerminalCommandConflictException(
                printSourceId,
                terminalCommandId);
        }

        if (
            reservation.Status is not PrintReservationStatus.Reserved and
                not PrintReservationStatus.ResolutionRequired)
        {
            throw new PrintReservationTerminalCommandConflictException(
                printSourceId,
                terminalCommandId);
        }

        return null;
    }

    private async Task<PrintReservationResult> ReadPersistedResultAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        return await _reservationRepository.FindByIdAsync(
            reservationId,
            cancellationToken)
            ?? throw new InvalidDataException(
                $"Persisted print reservation '{reservationId}' was not found after update.");
    }

    private void StageResolutionAudit(
        PrintReservation reservation,
        DateTimeOffset occurredAt)
    {
        _auditTrail.Stage(AuditEntry.ForProcess(
            AuditActorProcessName,
            "print-reservation.resolution-required",
            "print-reservation",
            reservation.Id.ToString(),
            $"Rezervace {reservation.Id} pro tisk {reservation.JobUuid} v částce {reservation.Amount.MinorUnits} haléřů vyžaduje ruční rozhodnutí; stav {reservation.Status}.",
            occurredAt));
    }

    private void StageReserveAudit(PrintReservation reservation)
    {
        _auditTrail.Stage(AuditEntry.ForProcess(
            AuditActorProcessName,
            "print-reservation.reserved",
            "print-reservation",
            reservation.Id.ToString(),
            $"Rezervace {reservation.Id} pro tisk {reservation.JobUuid} v částce {reservation.Amount.MinorUnits} haléřů byla vytvořena; stav {reservation.Status}.",
            reservation.CreatedAt));
    }

    private void StageCaptureAudit(
        PrintReservation reservation,
        DateTimeOffset occurredAt)
    {
        _auditTrail.Stage(AuditEntry.ForProcess(
            AuditActorProcessName,
            "print-reservation.captured",
            "print-reservation",
            reservation.Id.ToString(),
            $"Rezervace {reservation.Id} pro tisk {reservation.JobUuid} v částce {reservation.Amount.MinorUnits} haléřů byla zaúčtována; stav {reservation.Status}, debit {reservation.DebitOperationId}.",
            occurredAt));
    }

    private void StageReleaseAudit(
        PrintReservation reservation,
        DateTimeOffset occurredAt)
    {
        _auditTrail.Stage(AuditEntry.ForProcess(
            AuditActorProcessName,
            "print-reservation.released",
            "print-reservation",
            reservation.Id.ToString(),
            $"Rezervace {reservation.Id} pro tisk {reservation.JobUuid} v částce {reservation.Amount.MinorUnits} haléřů byla uvolněna; stav {reservation.Status}.",
            occurredAt));
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

        var available = await _availabilityService.GetAvailableAsync(
            account,
            cancellationToken);

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

        StageReserveAudit(reservation);
        await _reservationRepository.AddAsync(
            reservation,
            cancellationToken);

        return await ReadPersistedResultAsync(
            reservation.Id,
            cancellationToken);
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

    private sealed record LockedReservation(
        CreditAccount Account,
        PrintReservation Reservation);
}
