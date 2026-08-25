namespace FuaPay.Web.Modules.Jobs.Application;

public interface IJobPaymentCoordination
{
    Task<bool> LockJobAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task<bool> HasBlockingDirectPaymentAsync(
        Guid jobId,
        CancellationToken cancellationToken);
}
