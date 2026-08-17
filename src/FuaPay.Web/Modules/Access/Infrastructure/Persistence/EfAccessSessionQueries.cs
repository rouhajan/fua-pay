using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class EfAccessSessionQueries : IAccessSessionQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfAccessSessionQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<AccessSessionSnapshot?> FindAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        var user = await _dbContext.AccessUsers
            .AsNoTracking()
            .Include(
                item => item.RoleAssignments
                    .Where(assignment => assignment.RevokedAt == null))
            .SingleOrDefaultAsync(
                item => item.Id == userId,
                cancellationToken);

        if (user is null)
        {
            return null;
        }

        return new AccessSessionSnapshot(
            user.Id,
            user.DisplayName,
            user.Email,
            (AccessUserStatus)user.Status,
            user.RoleAssignments
                .Select(assignment => (AccessRole)assignment.Role)
                .Distinct()
                .OrderBy(role => role)
                .ToArray());
    }
}
