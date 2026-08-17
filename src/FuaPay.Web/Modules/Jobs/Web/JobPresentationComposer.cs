using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Modules.Jobs.Web;

public sealed class JobPresentationComposer
{
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly IServiceUnitQueries _serviceUnitQueries;

    public JobPresentationComposer(
        IAccessUserQueries accessUserQueries,
        IServiceUnitQueries serviceUnitQueries)
    {
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        _accessUserQueries = accessUserQueries;
        _serviceUnitQueries = serviceUnitQueries;
    }

    public async Task<IReadOnlyList<JobListPresentation>> ComposeAsync(
        IReadOnlyList<JobListItem> jobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var users = await _accessUserQueries.FindOptionsAsync(
            jobs.SelectMany(
                job => new[]
                {
                    job.CustomerUserId,
                    job.CreatedByUserId
                }),
            cancellationToken);

        var units = (await _serviceUnitQueries.ListAllAsync(
                cancellationToken))
            .ToDictionary(item => item.Id);

        return jobs.Select(
            job =>
            {
                var unit = units.GetValueOrDefault(job.ServiceUnitId);

                return new JobListPresentation(
                    job,
                    unit?.Code ?? "?",
                    unit?.DisplayName ?? "Neznámé pracoviště",
                    users.GetValueOrDefault(job.CustomerUserId),
                    users.GetValueOrDefault(job.CreatedByUserId));
            })
            .ToArray();
    }

    public async Task<JobDetailPresentation> ComposeAsync(
        JobDetail job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var users = await _accessUserQueries.FindOptionsAsync(
            new[]
            {
                job.CustomerUserId,
                job.CreatedByUserId
            },
            cancellationToken);

        var unit = (await _serviceUnitQueries.ListAllAsync(
                cancellationToken))
            .SingleOrDefault(item => item.Id == job.ServiceUnitId);

        return new JobDetailPresentation(
            job,
            unit?.Code ?? "?",
            unit?.DisplayName ?? "Neznámé pracoviště",
            users.GetValueOrDefault(job.CustomerUserId),
            users.GetValueOrDefault(job.CreatedByUserId));
    }
}
