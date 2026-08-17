using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class EfAccessUserQueries : IAccessUserQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfAccessUserQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<AccessUserPage> ListAsync(
        AccessUserListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _dbContext.AccessUsers.AsNoTracking();

        if (request.Search is not null)
        {
            var pattern = $"%{request.Search}%";
            query = query.Where(
                user =>
                    EF.Functions.ILike(user.DisplayName, pattern) ||
                    (user.Email != null &&
                     EF.Functions.ILike(user.Email, pattern)));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var baseItems = await query
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .ThenBy(user => user.Id)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                user.Status,
                user.CreatedAt,
                user.LastSeenAt
            })
            .ToArrayAsync(cancellationToken);

        var userIds = baseItems.Select(item => item.Id).ToArray();

        var activeRoles = await _dbContext.AccessRoleAssignments
            .AsNoTracking()
            .Where(
                assignment =>
                    userIds.Contains(assignment.UserId) &&
                    assignment.RevokedAt == null)
            .Select(assignment => new
            {
                assignment.UserId,
                assignment.Role
            })
            .ToArrayAsync(cancellationToken);

        var rolesByUser = activeRoles
            .GroupBy(item => item.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AccessRole>)group
                    .Select(item => (AccessRole)item.Role)
                    .OrderBy(role => role)
                    .ToArray());

        var items = baseItems
            .Select(item => new AccessUserListItem(
                item.Id,
                item.DisplayName,
                item.Email,
                (AccessUserStatus)item.Status,
                item.CreatedAt,
                item.LastSeenAt,
                rolesByUser.GetValueOrDefault(item.Id, [])))
            .ToArray();

        return new AccessUserPage(
            items,
            request.Offset,
            request.Limit,
            totalCount);
    }

    public async Task<AccessUserDetail?> FindDetailAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var user = await _dbContext.AccessUsers
            .AsNoTracking()
            .Include(item => item.ExternalIdentities)
            .Include(item => item.RoleAssignments)
            .SingleOrDefaultAsync(
                item => item.Id == userId,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        var actorIds = user.RoleAssignments
            .SelectMany(
                assignment => new[]
                {
                    assignment.GrantedByUserId,
                    assignment.RevokedByUserId
                })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        var actorNames = actorIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.AccessUsers
                .AsNoTracking()
                .Where(item => actorIds.Contains(item.Id))
                .ToDictionaryAsync(
                    item => item.Id,
                    item => item.DisplayName,
                    cancellationToken);

        var roleAssignments = user.RoleAssignments
            .OrderByDescending(item => item.GrantedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new AccessRoleAssignmentReadModel(
                item.Id,
                (AccessRole)item.Role,
                item.GrantedAt,
                FormatActor(
                    item.GrantedByUserId,
                    item.GrantedByProcessName,
                    actorNames),
                item.RevokedAt,
                item.RevokedAt is null
                    ? null
                    : FormatActor(
                        item.RevokedByUserId,
                        item.RevokedByProcessName,
                        actorNames)))
            .ToArray();

        return new AccessUserDetail(
            user.Id,
            user.DisplayName,
            user.Email,
            (AccessUserStatus)user.Status,
            user.CreatedAt,
            user.LastSeenAt,
            user.Version,
            roleAssignments
                .Where(item => item.IsActive)
                .Select(item => item.Role)
                .OrderBy(role => role)
                .ToArray(),
            user.ExternalIdentities
                .OrderBy(item => item.Provider)
                .ThenBy(item => item.Tenant)
                .ThenBy(item => item.Subject)
                .Select(item => new AccessExternalIdentityReadModel(
                    item.Provider,
                    item.Tenant,
                    item.Subject))
                .ToArray(),
            roleAssignments);
    }

    public async Task<IReadOnlyList<AccessUserOption>>
        ListActiveCustomersAsync(
            CancellationToken cancellationToken = default)
    {
        var customerRole = (int)AccessRole.Customer;
        var activeStatus = (int)AccessUserStatus.Active;

        return await _dbContext.AccessUsers
            .AsNoTracking()
            .Where(
                user =>
                    user.Status == activeStatus &&
                    user.RoleAssignments.Any(
                        assignment =>
                            assignment.Role == customerRole &&
                            assignment.RevokedAt == null))
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .Select(user => new AccessUserOption(
                user.Id,
                user.DisplayName,
                user.Email))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, AccessUserOption>>
        FindOptionsAsync(
            IEnumerable<Guid> userIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var ids = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<Guid, AccessUserOption>();
        }

        return await _dbContext.AccessUsers
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new AccessUserOption(
                user.Id,
                user.DisplayName,
                user.Email))
            .ToDictionaryAsync(
                item => item.Id,
                cancellationToken);
    }


    public Task<bool> IsActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var activeStatus = (int)AccessUserStatus.Active;
        return _dbContext.AccessUsers
            .AsNoTracking()
            .AnyAsync(
                user =>
                    user.Id == userId &&
                    user.Status == activeStatus,
                cancellationToken);
    }

    public Task<bool> IsActiveCustomerAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var activeStatus = (int)AccessUserStatus.Active;
        var customerRole = (int)AccessRole.Customer;

        return _dbContext.AccessUsers
            .AsNoTracking()
            .AnyAsync(
                user =>
                    user.Id == userId &&
                    user.Status == activeStatus &&
                    user.RoleAssignments.Any(
                        assignment =>
                            assignment.Role == customerRole &&
                            assignment.RevokedAt == null),
                cancellationToken);
    }

    public Task<long> CountActiveUsersWithRoleAsync(
        AccessRole role,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        var roleValue = (int)role;
        var activeStatus = (int)AccessUserStatus.Active;

        return _dbContext.AccessUsers
            .AsNoTracking()
            .Where(
                user =>
                    user.Status == activeStatus &&
                    user.RoleAssignments.Any(
                        assignment =>
                            assignment.Role == roleValue &&
                            assignment.RevokedAt == null))
            .LongCountAsync(cancellationToken);
    }

    private static string FormatActor(
        Guid? userId,
        string? processName,
        IReadOnlyDictionary<Guid, string> actorNames)
    {
        if (userId.HasValue)
        {
            return actorNames.TryGetValue(
                userId.Value,
                out var displayName)
                    ? displayName
                    : userId.Value.ToString();
        }

        return processName ?? "Neznámý původce";
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }
    }
}
