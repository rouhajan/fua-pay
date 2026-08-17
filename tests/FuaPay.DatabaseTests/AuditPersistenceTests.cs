using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Audit.Application;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class AuditPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuditPersistenceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TrailAndQueries_RoundTripAuditEvent()
    {
        var entry = AuditEntry.ForUser(
            Guid.NewGuid(),
            "test.changed",
            "test-entity",
            Guid.NewGuid().ToString(),
            "Testovací auditní událost.",
            new DateTimeOffset(2026, 7, 29, 16, 30, 0, TimeSpan.Zero));

        try
        {
            using (var writeScope = _factory.Services.CreateScope())
            {
                await writeScope.ServiceProvider
                    .GetRequiredService<IAuditTrail>()
                    .WriteAsync(entry);
            }

            using var readScope = _factory.Services.CreateScope();
            var page = await readScope.ServiceProvider
                .GetRequiredService<IAuditQueries>()
                .ListAsync(
                    new AuditListFilter(Search: entry.EntityId),
                    new AuditPageRequest());

            var item = Assert.Single(page.Items);
            Assert.Equal(entry.Id, item.Id);
            Assert.Equal(entry.ActorUserId, item.ActorUserId);
            Assert.Equal(entry.Action, item.Action);
            Assert.Equal(entry.Description, item.Description);
        }
        finally
        {
            using var cleanupScope = _factory.Services.CreateScope();
            var dbContext = cleanupScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM audit.events WHERE id = {entry.Id}");
        }
    }
}
