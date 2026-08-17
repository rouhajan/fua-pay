namespace FuaPay.Web.Modules.Jobs.Application;

public interface IJobQueries
{
    Task<CustomerJobSummary> GetCustomerSummaryAsync(
        Guid customerUserId,
        CancellationToken cancellationToken = default);

    Task<JobDetail?> FindForCustomerAsync(
        Guid customerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<JobPage<JobListItem>> ListForCustomerAsync(
        Guid customerUserId,
        JobListFilter filter,
        JobPageRequest page,
        CancellationToken cancellationToken = default);

    Task<ManagementJobSummary> GetManagementSummaryAsync(
        JobManagementActor actor,
        CancellationToken cancellationToken = default);

    Task<JobDetail?> FindForManagementAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<JobPage<JobListItem>> ListForManagementAsync(
        JobManagementActor actor,
        JobListFilter filter,
        JobPageRequest page,
        CancellationToken cancellationToken = default);
}
