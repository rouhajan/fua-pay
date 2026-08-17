using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Notifications.Application;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class NotificationOutboxPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NotificationOutboxPersistenceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StagedMessage_IsSavedWithBusinessSaveChanges()
    {
        var message = NotificationMessage.Create(
            Guid.NewGuid(),
            "test.notification",
            "Testovací oznámení",
            "Text testovacího oznámení.",
            new DateTimeOffset(2026, 7, 29, 16, 45, 0, TimeSpan.Zero));

        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();
                var dbContext = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
                outbox.Stage(message);
                await dbContext.SaveChangesAsync();
            }

            using var readScope = _factory.Services.CreateScope();
            var items = await readScope.ServiceProvider
                .GetRequiredService<INotificationQueries>()
                .ListRecentAsync();

            var item = Assert.Single(items, item => item.Id == message.Id);
            Assert.Equal(message.RecipientUserId, item.RecipientUserId);
            Assert.Null(item.SentAt);
            Assert.Equal(0, item.AttemptCount);
        }
        finally
        {
            using var cleanupScope = _factory.Services.CreateScope();
            var dbContext = cleanupScope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM notifications.outbox WHERE id = {message.Id}");
        }
    }
}
