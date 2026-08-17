using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class EfAccessUserRepository :
    IAccessUserRepository
{
    private readonly FuaPayDbContext _dbContext;

    private readonly Dictionary<Guid, LoadedUserState>
        _loadedUsers = [];

    public EfAccessUserRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<AccessUser?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        var entity = await _dbContext.AccessUsers
            .AsNoTracking()
            .Include(user => user.RoleAssignments)
            .SingleOrDefaultAsync(
                user => user.Id == userId,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var user = Restore(entity);
        RememberLoadedState(user, entity.Version);

        return user;
    }

    public async Task<AccessUser?> FindByExternalIdentityAsync(
        ExternalIdentityKey identityKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        var identity =
            await _dbContext.AccessExternalIdentities
                .AsNoTracking()
                .Include(item => item.User)
                    .ThenInclude(user => user.RoleAssignments)
                .SingleOrDefaultAsync(
                    item =>
                        item.Provider == identityKey.Provider &&
                        item.Tenant == identityKey.Tenant &&
                        item.Subject == identityKey.Subject,
                    cancellationToken);

        if (identity is null)
        {
            return null;
        }

        var user = Restore(identity.User);

        RememberLoadedState(
            user,
            identity.User.Version);

        return user;
    }

    public async Task AddAsync(
        AccessUser user,
        ExternalIdentityKey identityKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(identityKey);

        var entity =
            CreateNewEntity(
                user,
                identityKey);

        _dbContext.AccessUsers.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                ExternalIdentityConfiguration
                    .PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new AccessIdentityConcurrencyException(
                identityKey,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                RoleAssignmentConfiguration
                    .ActiveRoleUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new AccessUserConcurrencyException(
                user.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();

        RememberLoadedState(
            user,
            entity.Version);
    }

    public async Task SaveAsync(
        AccessUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!_loadedUsers.TryGetValue(
            user.Id,
            out var loadedState))
        {
            throw new InvalidOperationException(
                "Uživatel musí být před uložením načten " +
                "stejnou instancí repozitáře.");
        }

        var nextVersion =
            checked(loadedState.Version + 1);

        var userEntity = new AccessUserEntity
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Status = (int)user.Status,
            CreatedAt = user.CreatedAt,
            LastSeenAt = user.LastSeenAt,
            Version = nextVersion
        };

        _dbContext.Attach(userEntity);

        var userEntry =
            _dbContext.Entry(userEntity);

        userEntry
            .Property(entity => entity.DisplayName)
            .IsModified = true;

        userEntry
            .Property(entity => entity.Email)
            .IsModified = true;

        userEntry
            .Property(entity => entity.Status)
            .IsModified = true;

        userEntry
            .Property(entity => entity.LastSeenAt)
            .IsModified = true;

        userEntry
            .Property(entity => entity.Version)
            .OriginalValue = loadedState.Version;

        userEntry
            .Property(entity => entity.Version)
            .IsModified = true;

        var currentAssignments =
            user.RoleAssignments.ToDictionary(
                assignment => assignment.Id);

        var missingAssignmentIds =
            loadedState.RevokedAtByAssignmentId.Keys
                .Where(
                    assignmentId =>
                        !currentAssignments.ContainsKey(
                            assignmentId))
                .ToArray();

        if (missingAssignmentIds.Length > 0)
        {
            _dbContext.ChangeTracker.Clear();

            throw new InvalidOperationException(
                "Historické přiřazení role nesmí být " +
                "z agregátu odstraněno.");
        }

        foreach (var assignment in user.RoleAssignments)
        {
            if (
                !loadedState.RevokedAtByAssignmentId.TryGetValue(
                    assignment.Id,
                    out var loadedRevokedAt))
            {
                _dbContext.AccessRoleAssignments.Add(
                    CreateRoleAssignmentEntity(
                        user.Id,
                        assignment));

                continue;
            }

            if (loadedRevokedAt == assignment.RevokedAt)
            {
                continue;
            }

            if (
                loadedRevokedAt is not null ||
                assignment.RevokedAt is null)
            {
                _dbContext.ChangeTracker.Clear();

                throw new InvalidOperationException(
                    "Historické odebrání role nesmí být " +
                    "změněno ani odstraněno.");
            }

            var revokedEntity =
                new RoleAssignmentEntity
                {
                    Id = assignment.Id,
                    RevokedAt = assignment.RevokedAt
                };

            ApplyRevokedActor(
                revokedEntity,
                assignment.RevokedBy);

            _dbContext.Attach(revokedEntity);

            var assignmentEntry =
                _dbContext.Entry(revokedEntity);

            assignmentEntry
                .Property(entity => entity.RevokedAt)
                .IsModified = true;

            assignmentEntry
                .Property(entity => entity.RevokedByType)
                .IsModified = true;

            assignmentEntry
                .Property(entity => entity.RevokedByUserId)
                .IsModified = true;

            assignmentEntry
                .Property(entity => entity.RevokedByProcessName)
                .IsModified = true;
        }

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();

            throw new AccessUserConcurrencyException(
                user.Id,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                RoleAssignmentConfiguration
                    .ActiveRoleUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new AccessUserConcurrencyException(
                user.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();

        RememberLoadedState(
            user,
            nextVersion);
    }

    private void RememberLoadedState(
        AccessUser user,
        long version)
    {
        _loadedUsers[user.Id] =
            new LoadedUserState(
                version,
                user.RoleAssignments.ToDictionary(
                    assignment => assignment.Id,
                    assignment => assignment.RevokedAt));
    }

    private static AccessUser Restore(
        AccessUserEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Uživatel '{entity.Id}' má neplatnou verzi.");
        }

        try
        {
            var user = new AccessUser(
                entity.Id,
                entity.DisplayName,
                entity.Email,
                entity.CreatedAt);

            user.SynchronizeProfile(
                entity.DisplayName,
                entity.Email,
                entity.LastSeenAt);

            foreach (var roleEntity in
                entity.RoleAssignments
                    .OrderBy(
                        assignment =>
                            assignment.GrantedAt)
                    .ThenBy(
                        assignment =>
                            assignment.RevokedAt is null
                                ? 1
                                : 0)
                    .ThenBy(
                        assignment =>
                            assignment.RevokedAt)
                    .ThenBy(
                        assignment =>
                            assignment.Id))
            {
                var role =
                    (AccessRole)roleEntity.Role;

                user.GrantRole(
                    roleEntity.Id,
                    role,
                    roleEntity.GrantedAt,
                    RestoreActor(
                        roleEntity.GrantedByType,
                        roleEntity.GrantedByUserId,
                        roleEntity.GrantedByProcessName));

                if (roleEntity.RevokedAt is null)
                {
                    if (
                        roleEntity.RevokedByType is not null ||
                        roleEntity.RevokedByUserId is not null ||
                        roleEntity.RevokedByProcessName is not null)
                    {
                        throw new InvalidDataException(
                            $"Přiřazení role '{roleEntity.Id}' " +
                            "obsahuje neúplné údaje o odebrání.");
                    }

                    continue;
                }

                if (roleEntity.RevokedByType is null)
                {
                    throw new InvalidDataException(
                        $"Přiřazení role '{roleEntity.Id}' " +
                        "nemá původce odebrání.");
                }

                user.RevokeRole(
                    role,
                    roleEntity.RevokedAt.Value,
                    RestoreActor(
                        roleEntity.RevokedByType.Value,
                        roleEntity.RevokedByUserId,
                        roleEntity.RevokedByProcessName));
            }

            switch ((AccessUserStatus)entity.Status)
            {
                case AccessUserStatus.Active:
                    break;

                case AccessUserStatus.Blocked:
                    user.Block();
                    break;

                default:
                    throw new InvalidDataException(
                        $"Uživatel '{entity.Id}' má neplatný stav.");
            }

            return user;
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
                $"Uživatel '{entity.Id}' obsahuje " +
                "nekonzistentní data.",
                exception);
        }
    }

    private static RoleChangeActor RestoreActor(
        int actorType,
        Guid? userId,
        string? processName)
    {
        return (RoleChangeActorType)actorType switch
        {
            RoleChangeActorType.User
                when
                    userId.HasValue &&
                    userId.Value != Guid.Empty &&
                    processName is null =>
                RoleChangeActor.ForUser(
                    userId.Value),

            RoleChangeActorType.Process
                when
                    userId is null &&
                    !string.IsNullOrWhiteSpace(
                        processName) =>
                RoleChangeActor.ForProcess(
                    processName),

            _ => throw new InvalidDataException(
                "Původce změny role má neplatnou podobu.")
        };
    }

    private static AccessUserEntity CreateNewEntity(
        AccessUser user,
        ExternalIdentityKey identityKey)
    {
        var entity = new AccessUserEntity
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Status = (int)user.Status,
            CreatedAt = user.CreatedAt,
            LastSeenAt = user.LastSeenAt,
            Version = 1
        };

        entity.ExternalIdentities.Add(
            new ExternalIdentityEntity
            {
                Provider = identityKey.Provider,
                Tenant = identityKey.Tenant,
                Subject = identityKey.Subject,
                UserId = user.Id
            });

        foreach (var assignment in user.RoleAssignments)
        {
            entity.RoleAssignments.Add(
                CreateRoleAssignmentEntity(
                    user.Id,
                    assignment));
        }

        return entity;
    }

    private static RoleAssignmentEntity
        CreateRoleAssignmentEntity(
            Guid userId,
            RoleAssignment assignment)
    {
        var entity = new RoleAssignmentEntity
        {
            Id = assignment.Id,
            UserId = userId,
            Role = (int)assignment.Role,
            GrantedAt = assignment.GrantedAt,
            RevokedAt = assignment.RevokedAt
        };

        ApplyGrantedActor(
            entity,
            assignment.GrantedBy);

        ApplyRevokedActor(
            entity,
            assignment.RevokedBy);

        return entity;
    }

    private static void ApplyGrantedActor(
        RoleAssignmentEntity entity,
        RoleChangeActor actor)
    {
        entity.GrantedByType = (int)actor.Type;
        entity.GrantedByUserId = actor.UserId;
        entity.GrantedByProcessName = actor.ProcessName;
    }

    private static void ApplyRevokedActor(
        RoleAssignmentEntity entity,
        RoleChangeActor? actor)
    {
        if (actor is null)
        {
            entity.RevokedByType = null;
            entity.RevokedByUserId = null;
            entity.RevokedByProcessName = null;
            return;
        }

        entity.RevokedByType = (int)actor.Type;
        entity.RevokedByUserId = actor.UserId;
        entity.RevokedByProcessName = actor.ProcessName;
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } postgresException &&
            postgresException.ConstraintName == constraintName;
    }

    private sealed record LoadedUserState(
        long Version,
        Dictionary<Guid, DateTimeOffset?>
            RevokedAtByAssignmentId);
}
