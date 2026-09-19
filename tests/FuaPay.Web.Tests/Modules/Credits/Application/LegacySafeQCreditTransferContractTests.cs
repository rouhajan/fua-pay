using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class LegacySafeQCreditTransferContractTests
{
    private const string ApplicationNamespace =
        "FuaPay.Web.Modules.Credits.Application";

    [Fact]
    public void DedicatedApplicationTypes_ArePresent()
    {
        AssertRequiredType(
            $"{ApplicationNamespace}.LegacySafeQCreditTransferCommand");
        AssertRequiredType(
            $"{ApplicationNamespace}.LegacySafeQCreditTransferService");
        AssertRequiredType(
            $"{ApplicationNamespace}.ILegacySafeQCreditTransferRepository");
    }

    [Fact]
    public void Command_ExposesRequiredMigrationIdentityFields()
    {
        var commandType = AssertRequiredType(
            $"{ApplicationNamespace}.LegacySafeQCreditTransferCommand");

        AssertPublicProperty(commandType, "CommandId", typeof(Guid));
        AssertPublicProperty(
            commandType,
            "AdministratorUserId",
            typeof(Guid));
        AssertPublicProperty(commandType, "OwnerId", typeof(Guid));
        AssertPublicProperty(commandType, "SafeQUserId", typeof(string));
        AssertPublicProperty(commandType, "SnapshotSha256", typeof(string));
        AssertPublicProperty(commandType, "Amount", typeof(Money));
    }

    [Fact]
    public void PersistenceModel_EnforcesOneTransferPerSafeQUserId()
    {
        var options = new DbContextOptionsBuilder<FuaPayDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;

        using var dbContext = new FuaPayDbContext(options);

        var entityType = dbContext.Model
            .GetEntityTypes()
            .SingleOrDefault(
                item =>
                    string.Equals(
                        item.GetSchema(),
                        "credits",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        item.GetTableName(),
                        "legacy_safeq_credit_transfers",
                        StringComparison.Ordinal));

        Assert.True(
            entityType is not null,
            "Expected dedicated credits.legacy_safeq_credit_transfers persistence.");

        var requiredProperties = new[]
        {
            "CommandId",
            "AdministratorUserId",
            "OwnerId",
            "SafeQUserId",
            "SnapshotSha256",
            "AmountMinorUnits",
            "AcceptedAt"
        };

        foreach (var propertyName in requiredProperties)
        {
            Assert.True(
                entityType.FindProperty(propertyName) is not null,
                $"Missing persisted SafeQ transfer property '{propertyName}'.");
        }

        var primaryKey = entityType.FindPrimaryKey();
        Assert.NotNull(primaryKey);
        Assert.Equal(
            ["CommandId"],
            primaryKey.Properties.Select(property => property.Name));

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Count == 1 &&
                string.Equals(
                    index.Properties[0].Name,
                    "SafeQUserId",
                    StringComparison.Ordinal));
    }

    private static Type AssertRequiredType(string fullName)
    {
        var type = typeof(CreditService).Assembly.GetType(
            fullName,
            throwOnError: false,
            ignoreCase: false);

        Assert.True(
            type is not null,
            $"Expected dedicated SafeQ transfer type '{fullName}'.");

        return type;
    }

    private static void AssertPublicProperty(
        Type type,
        string propertyName,
        Type propertyType)
    {
        var property = type.GetProperty(propertyName);

        Assert.True(
            property is not null,
            $"Expected public property '{type.FullName}.{propertyName}'.");
        Assert.Equal(propertyType, property.PropertyType);
    }
}
