namespace FuaPay.Web.BuildingBlocks.Application;

public interface IApplicationTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteTopLevelAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "This application transaction implementation does not support explicit top-level ownership.");
}
