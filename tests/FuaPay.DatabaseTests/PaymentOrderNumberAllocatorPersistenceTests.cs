using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PaymentOrderNumberAllocatorPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PaymentOrderNumberAllocatorPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AllocateAsync_ConcurrentCallsProduceUniqueIncreasingValues()
    {
        var before = await ReadCurrentValueAsync();

        try
        {
            var numbers = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => AllocateAsync()));

            Assert.Equal(8, numbers.Distinct().Count());
            Assert.All(
                numbers,
                value => Assert.InRange(
                    value,
                    1,
                    PaymentInitiation.MaximumOrderNumber));
            Assert.Equal(
                Enumerable.Range(1, 8)
                    .Select(offset => before + offset),
                numbers.OrderBy(value => value));
        }
        finally
        {
            await RestoreCurrentValueAsync(before);
        }
    }

    private async Task<long> AllocateAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var allocator = scope.ServiceProvider
            .GetRequiredService<IPaymentOrderNumberAllocator>();

        return await allocator.AllocateAsync();
    }

    private async Task RestoreCurrentValueAsync(long value)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        if (value == 0)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "DELETE FROM payments.order_number_sequence WHERE id = 1");
            return;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE payments.order_number_sequence
            SET last_value = {value}
            WHERE id = 1
            """);
    }
    private async Task<long> ReadCurrentValueAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        var values = await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT last_value AS "Value"
                FROM payments.order_number_sequence
                WHERE id = 1
                """)
            .ToListAsync();

        return values.SingleOrDefault();
    }
}
