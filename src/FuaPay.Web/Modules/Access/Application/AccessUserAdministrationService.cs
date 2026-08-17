using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed class AccessUserAdministrationService
{
    private readonly IAccessUserRepository _repository;
    private readonly IAccessUserQueries _queries;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;
    private readonly IApplicationTransaction _transaction;
    private readonly IAccessAdministrationLock _administrationLock;

    public AccessUserAdministrationService(
        IAccessUserRepository repository,
        IAccessUserQueries queries,
        IAuditTrail auditTrail,
        TimeProvider timeProvider,
        IApplicationTransaction transaction,
        IAccessAdministrationLock administrationLock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(administrationLock);

        _repository = repository;
        _queries = queries;
        _auditTrail = auditTrail;
        _timeProvider = timeProvider;
        _transaction = transaction;
        _administrationLock = administrationLock;
    }

    public async Task GrantRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        AccessRole role,
        CancellationToken cancellationToken = default)
    {
        ValidateAdministrationRequest(actorUserId, targetUserId);
        ValidateMutableRole(role);

        var user = await RequireUserAsync(
            targetUserId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        user.GrantRole(
            Guid.NewGuid(),
            role,
            now,
            RoleChangeActor.ForUser(actorUserId));

        _auditTrail.Stage(
            AuditEntry.ForUser(
                actorUserId,
                "access.role.granted",
                "access-user",
                targetUserId.ToString(),
                $"Přidělena role {role}.",
                now));

        await _repository.SaveAsync(user, cancellationToken);
    }

    public async Task RevokeRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        AccessRole role,
        CancellationToken cancellationToken = default)
    {
        ValidateAdministrationRequest(actorUserId, targetUserId);
        ValidateMutableRole(role);

        await _transaction.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await RevokeRoleInsideTransactionAsync(
                    actorUserId,
                    targetUserId,
                    role,
                    transactionCancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task RevokeRoleInsideTransactionAsync(
        Guid actorUserId,
        Guid targetUserId,
        AccessRole role,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(
            targetUserId,
            cancellationToken);

        if (
            role == AccessRole.Admin &&
            user.HasEffectiveRole(AccessRole.Admin))
        {
            await _administrationLock.AcquireAsync(cancellationToken);
            await EnsureAnotherAdministratorExistsAsync(
                cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        user.RevokeRole(
            role,
            now,
            RoleChangeActor.ForUser(actorUserId));

        _auditTrail.Stage(
            AuditEntry.ForUser(
                actorUserId,
                "access.role.revoked",
                "access-user",
                targetUserId.ToString(),
                $"Odebrána role {role}.",
                now));

        await _repository.SaveAsync(user, cancellationToken);
    }

    public async Task BlockAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateAdministrationRequest(actorUserId, targetUserId);

        if (actorUserId == targetUserId)
        {
            throw new SelfBlockNotAllowedException();
        }

        await _transaction.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await BlockInsideTransactionAsync(
                    actorUserId,
                    targetUserId,
                    transactionCancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task BlockInsideTransactionAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(
            targetUserId,
            cancellationToken);

        if (user.Status == AccessUserStatus.Blocked)
        {
            return;
        }

        if (user.HasEffectiveRole(AccessRole.Admin))
        {
            await _administrationLock.AcquireAsync(cancellationToken);
            await EnsureAnotherAdministratorExistsAsync(
                cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        user.Block();
        _auditTrail.Stage(
            AuditEntry.ForUser(
                actorUserId,
                "access.user.blocked",
                "access-user",
                targetUserId.ToString(),
                "Uživatel byl zablokován.",
                now));
        await _repository.SaveAsync(user, cancellationToken);
    }

    public async Task ActivateAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateAdministrationRequest(actorUserId, targetUserId);

        var user = await RequireUserAsync(
            targetUserId,
            cancellationToken);

        if (user.Status == AccessUserStatus.Active)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        user.Activate();
        _auditTrail.Stage(
            AuditEntry.ForUser(
                actorUserId,
                "access.user.activated",
                "access-user",
                targetUserId.ToString(),
                "Uživatel byl aktivován.",
                now));
        await _repository.SaveAsync(user, cancellationToken);
    }

    private async Task<AccessUser> RequireUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _repository.FindByIdAsync(
                userId,
                cancellationToken)
            ?? throw new AccessUserNotFoundException(userId);
    }

    private async Task EnsureAnotherAdministratorExistsAsync(
        CancellationToken cancellationToken)
    {
        var count = await _queries.CountActiveUsersWithRoleAsync(
            AccessRole.Admin,
            cancellationToken);

        if (count <= 1)
        {
            throw new LastAdministratorProtectionException();
        }
    }

    private static void ValidateAdministrationRequest(
        Guid actorUserId,
        Guid targetUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID administrátora nesmí být prázdné.",
                nameof(actorUserId));
        }

        if (targetUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(targetUserId));
        }
    }

    private static void ValidateMutableRole(AccessRole role)
    {
        if (role == AccessRole.Customer)
        {
            throw new ProtectedCustomerRoleException();
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }
    }
}
