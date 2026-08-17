using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Modules.ServiceUnits.Application;

public interface IRequesterServiceUnitAssignmentRepository
{
    Task<RequesterServiceUnitAssignment?> FindByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken);

    Task<RequesterServiceUnitAssignment?> FindActiveAsync(
        Guid serviceUnitId,
        Guid userId,
        CancellationToken cancellationToken);

    Task AddAsync(
        RequesterServiceUnitAssignment assignment,
        CancellationToken cancellationToken);

    Task SaveAsync(
        RequesterServiceUnitAssignment assignment,
        CancellationToken cancellationToken);
}
