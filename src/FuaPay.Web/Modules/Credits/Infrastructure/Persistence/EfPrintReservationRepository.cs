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
    private static readonly int[] BlockingStatusValues =
    [
        (int)PrintReservationStatus.Reserved,
        (int)PrintReservationStatus.ResolutionRequired
    ];

    private readonly FuaPayDbContext _dbContext;

    public EfPrintReservationRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
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

    public async Task<Money> GetBlockingAmountAsync(
        Guid creditAccountId,
        CancellationToken cancellationToken)
    {
        ValidateId(creditAccountId, nameof(creditAccountId));

        var amount = await _dbContext.PrintReservations
            .AsNoTracking()
            .Where(
                reservation =>
                    reservation.CreditAccountId == creditAccountId &&
                    BlockingStatusValues.Contains(reservation.Status))
            .SumAsync(
                reservation => reservation.AmountMinorUnits,
                cancellationToken);

        return new Money(amount);
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
    }

    private static PrintReservationResult ToResult(
        PrintReservationEntity entity)
    {
        var status = (PrintReservationStatus)entity.Status;

        if (
            status == PrintReservationStatus.Unknown ||
            !Enum.IsDefined(status) ||
            entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Print reservation '{entity.Id}' has an invalid persisted state.");
        }

        var normalizedJobUuid = IppJobUuid.Normalize(entity.JobUuid);

        if (!string.Equals(
            normalizedJobUuid,
            entity.JobUuid,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Print reservation '{entity.Id}' has a non-canonical job UUID.");
        }

        return new PrintReservationResult(
            entity.Id,
            entity.CreditAccountId,
            entity.PrintSourceId,
            entity.JobUuid,
            new Money(entity.AmountMinorUnits),
            status,
            entity.ReserveCommandId,
            entity.ResolutionCommandId,
            entity.TerminalCommandId,
            entity.DebitOperationId,
            entity.CreatedAt,
            entity.StateChangedAt,
            entity.Version);
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
