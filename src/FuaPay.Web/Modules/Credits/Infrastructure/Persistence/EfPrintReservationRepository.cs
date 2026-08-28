using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfPrintReservationRepository :
    IPrintReservationRepository
{
    private readonly FuaPayDbContext _dbContext;

    private readonly Dictionary<Guid, long> _loadedReservations = [];

    public EfPrintReservationRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<PrintReservationResult?> FindByIdAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        ValidateId(reservationId, nameof(reservationId));

        var entity = await _dbContext.PrintReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reservation => reservation.Id == reservationId,
                cancellationToken);

        return entity is null ? null : ToResult(entity);
    }

    public async Task<PrintReservation?> FindByIdForUpdateAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        ValidateId(reservationId, nameof(reservationId));
        EnsureActiveTransaction();

        var entity = await _dbContext.PrintReservations
            .FromSqlInterpolated(
                $"SELECT * FROM credits.print_reservations WHERE id = {reservationId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var reservation = ToDomain(entity);
        _loadedReservations[reservation.Id] = entity.Version;
        return reservation;
    }

    public async Task<PrintReservationResult?> FindByReserveCommandAsync(
        Guid printSourceId,
        Guid reserveCommandId,
        CancellationToken cancellationToken)
    {
        ValidateId(printSourceId, nameof(printSourceId));
        ValidateId(reserveCommandId, nameof(reserveCommandId));

        var entity = await _dbContext.PrintReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reservation =>
                    reservation.PrintSourceId == printSourceId &&
                    reservation.ReserveCommandId == reserveCommandId,
                cancellationToken);

        return entity is null ? null : ToResult(entity);
    }

    public async Task<PrintReservationResult?> FindByPrintJobAsync(
        Guid printSourceId,
        string jobUuid,
        CancellationToken cancellationToken)
    {
        ValidateId(printSourceId, nameof(printSourceId));
        var normalizedJobUuid = IppJobUuid.Normalize(jobUuid);

        var entity = await _dbContext.PrintReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reservation =>
                    reservation.PrintSourceId == printSourceId &&
                    reservation.JobUuid == normalizedJobUuid,
                cancellationToken);

        return entity is null ? null : ToResult(entity);
    }

    public async Task<PrintReservationResult?> FindByResolutionCommandAsync(
        Guid printSourceId,
        Guid resolutionCommandId,
        CancellationToken cancellationToken)
    {
        ValidateId(printSourceId, nameof(printSourceId));
        ValidateId(resolutionCommandId, nameof(resolutionCommandId));

        var entity = await _dbContext.PrintReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reservation =>
                    reservation.PrintSourceId == printSourceId &&
                    reservation.ResolutionCommandId == resolutionCommandId,
                cancellationToken);

        return entity is null ? null : ToResult(entity);
    }

    public async Task<PrintReservationResult?> FindByTerminalCommandAsync(
        Guid printSourceId,
        Guid terminalCommandId,
        CancellationToken cancellationToken)
    {
        ValidateId(printSourceId, nameof(printSourceId));
        ValidateId(terminalCommandId, nameof(terminalCommandId));

        var entity = await _dbContext.PrintReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reservation =>
                    reservation.PrintSourceId == printSourceId &&
                    reservation.TerminalCommandId == terminalCommandId,
                cancellationToken);

        return entity is null ? null : ToResult(entity);
    }

    public async Task AddAsync(
        PrintReservation reservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var entity = new PrintReservationEntity
        {
            Id = reservation.Id,
            CreditAccountId = reservation.CreditAccountId,
            PrintSourceId = reservation.PrintSourceId,
            JobUuid = reservation.JobUuid,
            AmountMinorUnits = reservation.Amount.MinorUnits,
            Status = (int)reservation.Status,
            ReserveCommandId = reservation.ReserveCommandId,
            ResolutionCommandId = reservation.ResolutionCommandId,
            TerminalCommandId = reservation.TerminalCommandId,
            DebitOperationId = reservation.DebitOperationId,
            CreatedAt = reservation.CreatedAt,
            StateChangedAt = reservation.StateChangedAt,
            Version = 1
        };

        _dbContext.PrintReservations.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                PrintReservationConfiguration
                    .ReserveCommandUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new PrintReservationReserveCommandAlreadyExistsException(
                reservation.PrintSourceId,
                reservation.ReserveCommandId,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                PrintReservationConfiguration.PrintJobUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new PrintReservationPrintJobAlreadyExistsException(
                reservation.PrintSourceId,
                reservation.JobUuid,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedReservations[reservation.Id] = 1;
    }

    public async Task SaveAsync(
        PrintReservation reservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        EnsureActiveTransaction();

        if (!_loadedReservations.TryGetValue(
            reservation.Id,
            out var loadedVersion))
        {
            throw new InvalidOperationException(
                "A print reservation must be locked before it can be saved.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = ToEntity(reservation, nextVersion);
        _dbContext.Attach(entity);
        var entry = _dbContext.Entry(entity);

        entry.Property(item => item.Status).IsModified = true;
        entry.Property(item => item.ResolutionCommandId).IsModified = true;
        entry.Property(item => item.TerminalCommandId).IsModified = true;
        entry.Property(item => item.DebitOperationId).IsModified = true;
        entry.Property(item => item.StateChangedAt).IsModified = true;
        entry.Property(item => item.Version).OriginalValue = loadedVersion;
        entry.Property(item => item.Version).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new PrintReservationConcurrencyException(
                reservation.Id,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                PrintReservationConfiguration
                    .ResolutionCommandUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new PrintReservationResolutionCommandAlreadyExistsException(
                reservation.PrintSourceId,
                reservation.ResolutionCommandId ?? Guid.Empty,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                PrintReservationConfiguration
                    .TerminalCommandUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new PrintReservationTerminalCommandAlreadyExistsException(
                reservation.PrintSourceId,
                reservation.TerminalCommandId ?? Guid.Empty,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                PrintReservationConfiguration
                    .DebitOperationUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateCreditOperationException(
                reservation.DebitOperationId ?? Guid.Empty);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedReservations[reservation.Id] = nextVersion;
    }

    private static PrintReservationResult ToResult(
        PrintReservationEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Print reservation '{entity.Id}' has an invalid persisted state.");
        }

        var reservation = ToDomain(entity);
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
            entity.Version);
    }

    private static PrintReservation ToDomain(
        PrintReservationEntity entity)
    {
        return PrintReservation.Restore(
            entity.Id,
            entity.CreditAccountId,
            entity.PrintSourceId,
            entity.JobUuid,
            new Money(entity.AmountMinorUnits),
            (PrintReservationStatus)entity.Status,
            entity.ReserveCommandId,
            entity.ResolutionCommandId,
            entity.TerminalCommandId,
            entity.DebitOperationId,
            entity.CreatedAt,
            entity.StateChangedAt);
    }

    private static PrintReservationEntity ToEntity(
        PrintReservation reservation,
        long version)
    {
        return new PrintReservationEntity
        {
            Id = reservation.Id,
            CreditAccountId = reservation.CreditAccountId,
            PrintSourceId = reservation.PrintSourceId,
            JobUuid = reservation.JobUuid,
            AmountMinorUnits = reservation.Amount.MinorUnits,
            Status = (int)reservation.Status,
            ReserveCommandId = reservation.ReserveCommandId,
            ResolutionCommandId = reservation.ResolutionCommandId,
            TerminalCommandId = reservation.TerminalCommandId,
            DebitOperationId = reservation.DebitOperationId,
            CreatedAt = reservation.CreatedAt,
            StateChangedAt = reservation.StateChangedAt,
            Version = version
        };
    }

    private void EnsureActiveTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A print reservation lock or update requires an active database transaction.");
        }
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException &&
        postgresException.ConstraintName == constraintName;
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "The identifier must not be empty.",
                parameterName);
        }
    }
}
