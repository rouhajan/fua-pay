using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class JobNumberAllocatorPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JobNumberAllocatorPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AllocateAsync_ConcurrentCallsProduceUniqueSequence()
    {
        var serviceUnitId = Guid.NewGuid();
        const int year = 2026;

        try
        {
            var numbers = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(
                        _ => AllocateAsync(
                            serviceUnitId,
                            "NUM",
                            year)));

            Assert.Equal(8, numbers.Distinct().Count());
            Assert.Equal(
                Enumerable.Range(1, 8)
                    .Select(value => $"NUM-{year}-{value:D6}")
                    .OrderBy(value => value),
                numbers.OrderBy(value => value));
        }
        finally
        {
            await DeleteSequenceAsync(serviceUnitId, year);
        }
    }


    [Theory]
    [InlineData("A")]
    [InlineData("TOO-LONG")]
    [InlineData("3D_")]
    public async Task AllocateAsync_InvalidCodeIsRejected(
        string code)
    {
        using var scope = _factory.Services.CreateScope();
        var allocator = scope.ServiceProvider
            .GetRequiredService<IJobNumberAllocator>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => allocator.AllocateAsync(
                Guid.NewGuid(),
                code,
                2026));
    }

    [Fact]
    public async Task EnsureAtLeastAsync_NeverMovesSequenceBackwards()
    {
        var serviceUnitId = Guid.NewGuid();
        const int year = 2026;

        try
        {
            using var scope = _factory.Services.CreateScope();
            var allocator = scope.ServiceProvider
                .GetRequiredService<IJobNumberAllocator>();

            await allocator.EnsureAtLeastAsync(
                serviceUnitId,
                year,
                12);

            await allocator.EnsureAtLeastAsync(
                serviceUnitId,
                year,
                4);

            var number = await allocator.AllocateAsync(
                serviceUnitId,
                "NUM",
                year);

            Assert.Equal("NUM-2026-000013", number);
        }
        finally
        {
            await DeleteSequenceAsync(serviceUnitId, year);
        }
    }

    private async Task<string> AllocateAsync(
        Guid serviceUnitId,
        string code,
        int year)
    {
        using var scope = _factory.Services.CreateScope();
        var allocator = scope.ServiceProvider
            .GetRequiredService<IJobNumberAllocator>();

        return await allocator.AllocateAsync(
            serviceUnitId,
            code,
            year);
    }

    private async Task DeleteSequenceAsync(
        Guid serviceUnitId,
        int year)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM jobs.job_number_sequences
            WHERE service_unit_id = {serviceUnitId}
              AND year = {year}
            """);
    }
}
