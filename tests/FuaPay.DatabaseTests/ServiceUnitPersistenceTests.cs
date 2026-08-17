using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class ServiceUnitPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public ServiceUnitPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Queries_ReturnSharedActiveRequesterScope()
    {
        var serviceUnitId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();

        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var unitRepository =
                    scope.ServiceProvider.GetRequiredService<
                        IServiceUnitRepository>();

                var assignmentRepository =
                    scope.ServiceProvider.GetRequiredService<
                        IRequesterServiceUnitAssignmentRepository>();

                var actor = ServiceUnitChangeActor.ForProcess(
                    "database-test");

                await unitRepository.AddAsync(
                    new ServiceUnit(
                        serviceUnitId,
                        $"T{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
                        "Testovací pracoviště",
                        ServiceType.Other,
                        CreatedAt,
                        actor),
                    CancellationToken.None);

                await assignmentRepository.AddAsync(
                    new RequesterServiceUnitAssignment(
                        Guid.NewGuid(),
                        serviceUnitId,
                        firstUserId,
                        CreatedAt.AddMinutes(1),
                        actor),
                    CancellationToken.None);

                await assignmentRepository.AddAsync(
                    new RequesterServiceUnitAssignment(
                        Guid.NewGuid(),
                        serviceUnitId,
                        secondUserId,
                        CreatedAt.AddMinutes(1),
                        actor),
                    CancellationToken.None);
            }

            using var queryScope = _factory.Services.CreateScope();

            var queries = queryScope.ServiceProvider
                .GetRequiredService<IServiceUnitQueries>();

            var activeUnit = await queries.FindActiveAsync(
                serviceUnitId);

            var firstScope = await queries.ListForRequesterAsync(
                firstUserId);

            var secondScope = await queries.ListForRequesterAsync(
                secondUserId);

            Assert.Equal(serviceUnitId, activeUnit!.Id);
            Assert.Equal(
                ServiceType.Other,
                activeUnit.DefaultServiceType);
            Assert.Equal(serviceUnitId, Assert.Single(firstScope).Id);
            Assert.Equal(serviceUnitId, Assert.Single(secondScope).Id);
        }
        finally
        {
            using var cleanupScope = _factory.Services.CreateScope();

            var dbContext = cleanupScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM service_units.requester_assignments
                WHERE service_unit_id = {serviceUnitId}
                """);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM service_units.units
                WHERE id = {serviceUnitId}
                """);
        }
    }
}
