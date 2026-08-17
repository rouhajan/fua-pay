using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Modules.ServiceUnits.Application;

public interface IServiceUnitRepository
{
    Task<ServiceUnit?> FindByIdAsync(
        Guid serviceUnitId,
        CancellationToken cancellationToken);

    Task<ServiceUnit?> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken);

    Task AddAsync(
        ServiceUnit serviceUnit,
        CancellationToken cancellationToken);

    Task SaveAsync(
        ServiceUnit serviceUnit,
        CancellationToken cancellationToken);
}
