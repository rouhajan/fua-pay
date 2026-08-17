namespace FuaPay.Web.BuildingBlocks.Application;

public interface IApplicationTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
