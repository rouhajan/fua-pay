using FuaPay.Web.BuildingBlocks.Persistence;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Tests.BuildingBlocks.Persistence;

public sealed class PersistenceModelTests
{
    [Fact]
    public void ExternalIdentities_AllowOnlyOneIdentityPerUserProviderTenant()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "access" &&
                    type.GetTableName() == "external_identities");

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetDatabaseName() ==
                    "uq_access_external_identities_user_provider_tenant" &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["UserId", "Provider", "Tenant"]));
    }

    [Fact]
    public void CreditsAccount_HasExpectedSchemaAndConcurrencyToken()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "credits" &&
                    type.GetTableName() == "accounts");

        Assert.True(
            entityType.FindProperty("Version")!
                .IsConcurrencyToken);

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["OwnerId"]));
    }

    [Fact]
    public void CreditsMovement_HasGlobalOperationAndAccountSequenceUniqueness()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "credits" &&
                    type.GetTableName() == "movements");

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["OperationId"]));

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(
                        ["AccountId", "Sequence"]));

        var foreignKey = Assert.Single(
            entityType.GetForeignKeys());

        Assert.Equal(
            DeleteBehavior.Restrict,
            foreignKey.DeleteBehavior);
    }

    [Fact]
    public void CreditsPrintReservations_HaveExpectedSchemaRelationshipsAndIndexes()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "credits" &&
                    type.GetTableName() == "print_reservations");

        Assert.True(
            entityType.FindProperty("Version")!
                .IsConcurrencyToken);
        Assert.Equal(
            45,
            entityType.FindProperty("JobUuid")!
                .GetMaxLength());

        var foreignKey = Assert.Single(
            entityType.GetForeignKeys());

        Assert.Equal(
            DeleteBehavior.Restrict,
            foreignKey.DeleteBehavior);

        (string Name, string[] Properties, string? Filter)[]
            expectedUniqueIndexes =
            [
                (
                    "uq_credits_print_reservations_print_job",
                    ["PrintSourceId", "JobUuid"],
                    null),
                (
                    "uq_credits_print_reservations_reserve_command",
                    ["PrintSourceId", "ReserveCommandId"],
                    null),
                (
                    "uq_credits_print_reservations_resolution_command",
                    ["PrintSourceId", "ResolutionCommandId"],
                    "resolution_command_id IS NOT NULL"),
                (
                    "uq_credits_print_reservations_terminal_command",
                    ["PrintSourceId", "TerminalCommandId"],
                    "terminal_command_id IS NOT NULL"),
                (
                    "uq_credits_print_reservations_debit_operation",
                    ["DebitOperationId"],
                    "debit_operation_id IS NOT NULL")
            ];

        foreach (var expected in expectedUniqueIndexes)
        {
            Assert.Contains(
                entityType.GetIndexes(),
                index =>
                    index.IsUnique &&
                    index.GetDatabaseName() == expected.Name &&
                    index.GetFilter() == expected.Filter &&
                    index.Properties
                        .Select(property => property.Name)
                        .SequenceEqual(expected.Properties));
        }

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                !index.IsUnique &&
                index.GetDatabaseName() ==
                    "ix_credits_print_reservations_account_status" &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(
                        ["CreditAccountId", "Status"]));
    }

    [Fact]
    public void ServiceUnits_HaveUniqueCodeAndConcurrencyToken()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "service_units" &&
                    type.GetTableName() == "units");

        Assert.True(
            entityType.FindProperty("Version")!
                .IsConcurrencyToken);

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["Code"]));
    }

    [Fact]
    public void ServiceUnitAssignments_HaveUniqueActiveScope()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "service_units" &&
                    type.GetTableName() == "requester_assignments");

        Assert.True(
            entityType.FindProperty("Version")!
                .IsConcurrencyToken);

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetFilter() == "revoked_at IS NULL" &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["ServiceUnitId", "UserId"]));

        var foreignKey = Assert.Single(entityType.GetForeignKeys());

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }


    [Fact]
    public void Jobs_HaveServiceUnitScopeNumberAndConcurrencyToken()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "jobs" &&
                    type.GetTableName() == "jobs");

        Assert.True(
            entityType.FindProperty("Version")!
                .IsConcurrencyToken);

        Assert.NotNull(entityType.FindProperty("Number"));
        Assert.NotNull(entityType.FindProperty("ServiceUnitId"));
        Assert.NotNull(entityType.FindProperty("CreatedByUserId"));
        Assert.Null(entityType.FindProperty("RequesterUserId"));

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["Number"]));

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(
                        ["ServiceUnitId", "CreatedAt"]));
    }

    [Fact]
    public void JobNumberSequences_HavePerUnitAndYearPrimaryKey()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "jobs" &&
                    type.GetTableName() ==
                        "job_number_sequences");

        Assert.Equal(
            new[] { "ServiceUnitId", "Year" },
            entityType.FindPrimaryKey()!
                .Properties
                .Select(property => property.Name)
                .ToArray());
    }


    [Fact]
    public void Payments_HaveCreationRequestAndBlockingJobUniqueness()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "payments" &&
                    type.GetTableName() == "payments");

        Assert.True(
            entityType.FindProperty("Version")!
                .IsConcurrencyToken);

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetFilter() == "provider_reference IS NOT NULL" &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["Provider", "ProviderReference"]));

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetFilter() ==
                    "job_id IS NOT NULL AND status IN (1, 2, 3)" &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["JobId"]));

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetFilter() == "creation_request_id IS NOT NULL" &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["CreationRequestId"]));

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.GetDatabaseName() ==
                    "ix_payments_csob_pending_long_open" &&
                index.GetFilter() ==
                    "provider = 2 AND status = 2 AND provider_reference IS NOT NULL" &&
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["UpdatedAt", "Id"]));
    }

    [Fact]
    public void CreditAdjustmentCommands_HaveCommandPrimaryKeyAndPayload()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "credits" &&
                    type.GetTableName() == "adjustment_commands");

        Assert.Equal(
            ["CommandId"],
            entityType.FindPrimaryKey()!
                .Properties
                .Select(property => property.Name)
                .ToArray());
        Assert.NotNull(entityType.FindProperty("AdministratorUserId"));
        Assert.NotNull(entityType.FindProperty("OwnerId"));
        Assert.NotNull(entityType.FindProperty("SignedAmountMinorUnits"));
        Assert.Equal(
            300,
            entityType.FindProperty("Reason")!.GetMaxLength());
        Assert.NotNull(entityType.FindProperty("AcceptedAt"));
    }


    [Fact]
    public void AuditEvents_AreImmutableAndIndexed()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "audit" &&
                    type.GetTableName() == "events");

        Assert.Null(entityType.FindProperty("Version"));
        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["OccurredAt"]));
        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["EntityType", "EntityId", "OccurredAt"]));
    }


    [Fact]
    public void NotificationOutbox_IsIndexedForPendingDelivery()
    {
        using var context = CreateContext();

        var entityType = context.Model
            .GetEntityTypes()
            .Single(
                type =>
                    type.GetSchema() == "notifications" &&
                    type.GetTableName() == "outbox");

        Assert.Contains(
            entityType.GetIndexes(),
            index =>
                index.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(["SentAt", "CreatedAt"]));
    }

    private static FuaPayDbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<FuaPayDbContext>()
                .UseNpgsql(
                    "Host=localhost;" +
                    "Database=unused;" +
                    "Username=unused;" +
                    "Password=unused")
                .Options;

        return new FuaPayDbContext(options);
    }
}
