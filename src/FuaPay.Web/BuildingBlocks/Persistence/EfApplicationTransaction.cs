using FuaPay.Web.BuildingBlocks.Application;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.BuildingBlocks.Persistence;

internal sealed class EfApplicationTransaction :
    IApplicationTransaction
{
    private readonly FuaPayDbContext _dbContext;

    public EfApplicationTransaction(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        return await ExecuteNewAsync(operation, cancellationToken);
    }

    public Task<T> ExecuteTopLevelAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A top-level application transaction cannot start while another transaction is active.");
        }

        return ExecuteNewAsync(operation, cancellationToken);
    }

    private async Task<T> ExecuteNewAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var result =
                await operation(cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);
            }
            finally
            {
                _dbContext.ChangeTracker.Clear();
            }

            throw;
        }
    }
}
