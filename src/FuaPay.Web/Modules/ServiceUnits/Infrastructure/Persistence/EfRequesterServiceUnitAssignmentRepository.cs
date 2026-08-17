using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

internal sealed class EfRequesterServiceUnitAssignmentRepository :
    IRequesterServiceUnitAssignmentRepository
{
    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfRequesterServiceUnitAssignmentRepository(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<RequesterServiceUnitAssignment?> FindByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        if (assignmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID přiřazení zadavatele nesmí být prázdné.",
                nameof(assignmentId));
        }

        var entity = await _dbContext.ServiceUnitRequesterAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == assignmentId,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task<RequesterServiceUnitAssignment?> FindActiveAsync(
        Guid serviceUnitId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ValidateIds(serviceUnitId, userId);

        var entity = await _dbContext.ServiceUnitRequesterAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.ServiceUnitId == serviceUnitId &&
                    item.UserId == userId &&
                    item.RevokedAt == null,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task AddAsync(
        RequesterServiceUnitAssignment assignment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        var entity = CreateEntity(assignment, version: 1);
        _dbContext.ServiceUnitRequesterAssignments.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                RequesterServiceUnitAssignmentConfiguration
                    .ActiveAssignmentUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new RequesterAlreadyAssignedException(
                assignment.ServiceUnitId,
                assignment.UserId);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                RequesterServiceUnitAssignmentConfiguration
                    .PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new RequesterAssignmentConcurrencyException(
                assignment.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[assignment.Id] = 1;
    }

    public async Task SaveAsync(
        RequesterServiceUnitAssignment assignment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        if (!_loadedVersions.TryGetValue(
            assignment.Id,
            out var loadedVersion))
        {
            throw new InvalidOperationException(
                "Přiřazení zadavatele musí být před uložením " +
                "načteno stejnou instancí repozitáře.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = CreateEntity(assignment, nextVersion);

        _dbContext.Attach(entity);
        var entry = _dbContext.Entry(entity);

        entry.Property(item => item.RevokedAt).IsModified = true;
        entry.Property(item => item.RevokedByType).IsModified = true;
        entry.Property(item => item.RevokedByUserId).IsModified = true;
        entry.Property(item => item.RevokedByProcessName).IsModified = true;
        entry.Property(item => item.Version).OriginalValue = loadedVersion;
        entry.Property(item => item.Version).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new RequesterAssignmentConcurrencyException(
                assignment.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[assignment.Id] = nextVersion;
    }

    private RequesterServiceUnitAssignment? RestoreAndRemember(
        RequesterServiceUnitAssignmentEntity? entity)
    {
        if (entity is null)
        {
            return null;
        }

        var assignment = Restore(entity);
        _loadedVersions[assignment.Id] = entity.Version;
        return assignment;
    }

    private static RequesterServiceUnitAssignment Restore(
        RequesterServiceUnitAssignmentEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Přiřazení zadavatele '{entity.Id}' má neplatnou verzi.");
        }

        try
        {
            var assignment = new RequesterServiceUnitAssignment(
                entity.Id,
                entity.ServiceUnitId,
                entity.UserId,
                entity.GrantedAt,
                EfServiceUnitRepository.RestoreActor(
                    entity.GrantedByType,
                    entity.GrantedByUserId,
                    entity.GrantedByProcessName));

            if (entity.RevokedAt is null)
            {
                if (
                    entity.RevokedByType is not null ||
                    entity.RevokedByUserId is not null ||
                    entity.RevokedByProcessName is not null)
                {
                    throw new InvalidDataException(
                        $"Aktivní přiřazení '{entity.Id}' obsahuje " +
                        "údaje o odebrání.");
                }

                return assignment;
            }

            if (entity.RevokedByType is null)
            {
                throw new InvalidDataException(
                    $"Odebrané přiřazení '{entity.Id}' nemá původce.");
            }

            assignment.Revoke(
                entity.RevokedAt.Value,
                EfServiceUnitRepository.RestoreActor(
                    entity.RevokedByType.Value,
                    entity.RevokedByUserId,
                    entity.RevokedByProcessName));

            return assignment;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (
                exception is ArgumentException or
                InvalidOperationException)
        {
            throw new InvalidDataException(
                $"Přiřazení zadavatele '{entity.Id}' obsahuje " +
                "nekonzistentní data.",
                exception);
        }
    }

    private static RequesterServiceUnitAssignmentEntity CreateEntity(
        RequesterServiceUnitAssignment assignment,
        long version)
    {
        var entity = new RequesterServiceUnitAssignmentEntity
        {
            Id = assignment.Id,
            ServiceUnitId = assignment.ServiceUnitId,
            UserId = assignment.UserId,
            GrantedAt = assignment.GrantedAt,
            RevokedAt = assignment.RevokedAt,
            Version = version
        };

        ApplyActor(entity, assignment.GrantedBy, granted: true);
        ApplyActor(entity, assignment.RevokedBy, granted: false);
        return entity;
    }

    private static void ApplyActor(
        RequesterServiceUnitAssignmentEntity entity,
        ServiceUnitChangeActor? actor,
        bool granted)
    {
        if (granted && actor is null)
        {
            throw new InvalidOperationException(
                "Přiřazení musí mít původce přidělení.");
        }

        if (granted)
        {
            entity.GrantedByType = (int)actor!.Type;
            entity.GrantedByUserId = actor.UserId;
            entity.GrantedByProcessName = actor.ProcessName;
            return;
        }

        entity.RevokedByType = actor is null
            ? null
            : (int)actor.Type;
        entity.RevokedByUserId = actor?.UserId;
        entity.RevokedByProcessName = actor?.ProcessName;
    }

    private static void ValidateIds(Guid serviceUnitId, Guid userId)
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return
            exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            string.Equals(
                postgres.ConstraintName,
                constraintName,
                StringComparison.Ordinal);
    }
}
