using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

internal sealed class EfServiceUnitQueries : IServiceUnitQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfServiceUnitQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ServiceUnitAdministrationListItem>>
        ListAllAsync(
            CancellationToken cancellationToken = default)
    {
        var activeStatus = (int)ServiceUnitStatus.Active;

        return await _dbContext.ServiceUnits
            .AsNoTracking()
            .OrderByDescending(item => item.Status == activeStatus)
            .ThenBy(item => item.DisplayName)
            .ThenBy(item => item.Code)
            .Select(item => new ServiceUnitAdministrationListItem(
                item.Id,
                item.Code,
                item.DisplayName,
                (ServiceType)item.DefaultServiceType,
                (ServiceUnitStatus)item.Status,
                item.CreatedAt,
                item.DeactivatedAt,
                item.RequesterAssignments.LongCount(
                    assignment => assignment.RevokedAt == null)))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>>
        ListAssignmentsForUserAsync(
            Guid userId,
            bool includeRevoked = false,
            CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        var query = _dbContext.ServiceUnitRequesterAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId);

        if (!includeRevoked)
        {
            query = query.Where(assignment => assignment.RevokedAt == null);
        }

        return await query
            .OrderBy(assignment => assignment.ServiceUnit.DisplayName)
            .ThenBy(assignment => assignment.ServiceUnit.Code)
            .ThenByDescending(assignment => assignment.GrantedAt)
            .Select(assignment => new RequesterServiceUnitAssignmentReadModel(
                assignment.Id,
                assignment.ServiceUnitId,
                assignment.ServiceUnit.Code,
                assignment.ServiceUnit.DisplayName,
                assignment.UserId,
                assignment.GrantedAt,
                assignment.RevokedAt))
            .ToArrayAsync(cancellationToken);
    }

    public Task<ServiceUnitReadModel?> FindActiveAsync(
        Guid serviceUnitId,
        CancellationToken cancellationToken = default)
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        return _dbContext.ServiceUnits
            .AsNoTracking()
            .Where(
                item =>
                    item.Id == serviceUnitId &&
                    item.Status == (int)ServiceUnitStatus.Active)
            .Select(item => new ServiceUnitReadModel(
                item.Id,
                item.Code,
                item.DisplayName,
                (ServiceType)item.DefaultServiceType))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ServiceUnits
            .AsNoTracking()
            .Where(item => item.Status == (int)ServiceUnitStatus.Active)
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Code)
            .Select(item => new ServiceUnitReadModel(
                item.Id,
                item.Code,
                item.DisplayName,
                (ServiceType)item.DefaultServiceType))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceUnitReadModel>>
        ListForRequesterAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        return await _dbContext.ServiceUnitRequesterAssignments
            .AsNoTracking()
            .Where(
                assignment =>
                    assignment.UserId == userId &&
                    assignment.RevokedAt == null &&
                    assignment.ServiceUnit.Status ==
                        (int)ServiceUnitStatus.Active)
            .OrderBy(assignment => assignment.ServiceUnit.DisplayName)
            .ThenBy(assignment => assignment.ServiceUnit.Code)
            .Select(assignment => new ServiceUnitReadModel(
                assignment.ServiceUnit.Id,
                assignment.ServiceUnit.Code,
                assignment.ServiceUnit.DisplayName,
                (ServiceType)assignment.ServiceUnit.DefaultServiceType))
            .ToArrayAsync(cancellationToken);
    }
}
