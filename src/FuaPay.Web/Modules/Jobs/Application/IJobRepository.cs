using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Modules.Jobs.Application;

public interface IJobRepository
{
    Task<Job?> FindByIdAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task AddAsync(
        Job job,
        CancellationToken cancellationToken);

    Task SaveAsync(
        Job job,
        CancellationToken cancellationToken);
}
