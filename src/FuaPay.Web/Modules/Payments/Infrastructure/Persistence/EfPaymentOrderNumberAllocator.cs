using System.Data;
using System.Data.Common;

using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfPaymentOrderNumberAllocator :
    IPaymentOrderNumberAllocator
{
    private readonly FuaPayDbContext _dbContext;

    public EfPaymentOrderNumberAllocator(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<long> AllocateAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var closeAfter = connection.State != ConnectionState.Open;

        if (closeAfter)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO payments.order_number_sequence
                    (id, last_value)
                VALUES
                    (1, 1)
                ON CONFLICT (id)
                DO UPDATE SET last_value =
                    payments.order_number_sequence.last_value + 1
                WHERE payments.order_number_sequence.last_value <
                    {PaymentInitiation.MaximumOrderNumber}
                RETURNING last_value;
                """;
            command.Transaction = _dbContext.Database
                .CurrentTransaction?
                .GetDbTransaction();

            var result = await command.ExecuteScalarAsync(
                cancellationToken);

            if (result is null or DBNull)
            {
                throw new InvalidOperationException(
                    "Číselná řada platebních orderNo je vyčerpaná.");
            }

            return Convert.ToInt64(result);
        }
        finally
        {
            if (closeAfter)
            {
                await connection.CloseAsync();
            }
        }
    }
}
