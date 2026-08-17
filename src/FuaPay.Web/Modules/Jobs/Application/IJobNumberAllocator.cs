namespace FuaPay.Web.Modules.Jobs.Application;

public interface IJobNumberAllocator
{
    Task<string> AllocateAsync(
        Guid serviceUnitId,
        string serviceUnitCode,
        int year,
        CancellationToken cancellationToken = default);

    Task EnsureAtLeastAsync(
        Guid serviceUnitId,
        int year,
        int value,
        CancellationToken cancellationToken = default);
}
