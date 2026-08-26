using FuaPay.Web.BuildingBlocks.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace FuaPay.DatabaseTests;

public sealed class PrintReservationPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PrintReservationPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(1, false, false, false)]
    [InlineData(2, true, false, false)]
    [InlineData(3, false, true, true)]
    [InlineData(3, true, true, true)]
    [InlineData(4, false, true, false)]
    [InlineData(4, true, true, false)]
    public async Task SupportedStateShapes_AreAccepted(
        int status,
        bool hasResolutionCommand,
        bool hasTerminalCommand,
        bool hasDebitOperation)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var accountId = await InsertAccountAsync(dbContext);
            var row = CreateReservation(accountId) with
            {
                Status = status,
                ResolutionCommandId = hasResolutionCommand
                    ? Guid.NewGuid()
                    : null,
                TerminalCommandId = hasTerminalCommand
                    ? Guid.NewGuid()
                    : null,
                DebitOperationId = hasDebitOperation
                    ? Guid.NewGuid()
                    : null
            };

            await InsertReservationAsync(dbContext, row);

            var storedCount = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::integer AS "Value"
                    FROM credits.print_reservations
                    WHERE id = {row.Id}
                    """)
                .SingleAsync();

            Assert.Equal(1, storedCount);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("empty-id", "ck_credits_print_reservations_id_not_empty")]
    [InlineData("empty-account", "ck_credits_print_reservations_account_not_empty")]
    [InlineData("empty-source", "ck_credits_print_reservations_source_not_empty")]
    [InlineData("empty-reserve-command", "ck_credits_print_reservations_reserve_command_not_empty")]
    [InlineData("empty-resolution-command", "ck_credits_print_reservations_resolution_command_not_empty")]
    [InlineData("empty-terminal-command", "ck_credits_print_reservations_terminal_command_not_empty")]
    [InlineData("empty-debit-operation", "ck_credits_print_reservations_debit_operation_not_empty")]
    [InlineData("uppercase-job-uuid", "ck_credits_print_reservations_job_uuid_valid")]
    [InlineData("nil-job-uuid", "ck_credits_print_reservations_job_uuid_valid")]
    [InlineData("zero-amount", "ck_credits_print_reservations_amount_positive")]
    [InlineData("unknown-status", null)]
    [InlineData("reversed-time", "ck_credits_print_reservations_timestamps_ordered")]
    [InlineData("zero-version", "ck_credits_print_reservations_version_positive")]
    [InlineData("reserved-with-terminal", "ck_credits_print_reservations_state_consistent")]
    [InlineData("resolution-without-command", "ck_credits_print_reservations_state_consistent")]
    [InlineData("resolution-with-terminal", "ck_credits_print_reservations_state_consistent")]
    [InlineData("resolution-with-debit", "ck_credits_print_reservations_state_consistent")]
    [InlineData("captured-without-debit", "ck_credits_print_reservations_state_consistent")]
    [InlineData("released-with-debit", "ck_credits_print_reservations_state_consistent")]
    public async Task InvalidSchemaShapes_AreRejected(
        string invalidShape,
        string? expectedConstraint)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var accountId = await InsertAccountAsync(dbContext);
            var row = CreateInvalidReservation(
                CreateReservation(accountId),
                invalidShape);

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => InsertReservationAsync(dbContext, row));

            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                exception.SqlState);

            if (expectedConstraint is not null)
            {
                Assert.Equal(
                    expectedConstraint,
                    exception.ConstraintName);
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("print-job")]
    [InlineData("reserve-command")]
    [InlineData("resolution-command")]
    [InlineData("terminal-command")]
    [InlineData("debit-operation")]
    public async Task IdentityAndCommandUniquenessScopes_AreEnforced(
        string uniquenessScope)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var accountId = await InsertAccountAsync(dbContext);
            var original = CreateReservationForScope(
                accountId,
                uniquenessScope);

            await InsertReservationAsync(dbContext, original);

            if (uniquenessScope != "debit-operation")
            {
                var otherSource = CreateScopedDuplicate(
                    original,
                    uniquenessScope,
                    sameSource: false);

                await InsertReservationAsync(dbContext, otherSource);
            }

            var duplicate = CreateScopedDuplicate(
                original,
                uniquenessScope,
                sameSource:
                    uniquenessScope != "debit-operation");

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => InsertReservationAsync(dbContext, duplicate));

            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                exception.SqlState);
            Assert.Equal(
                ExpectedUniqueConstraint(uniquenessScope),
                exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<Guid> InsertAccountAsync(
        FuaPayDbContext dbContext)
    {
        var accountId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO credits.accounts
                (id, owner_id, balance_minor_units, version)
            VALUES
                ({accountId}, {ownerId}, 10_000, 1)
            """);

        return accountId;
    }

    private static Task<int> InsertReservationAsync(
        FuaPayDbContext dbContext,
        ReservationRow row)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO credits.print_reservations
            (
                id,
                credit_account_id,
                print_source_id,
                job_uuid,
                amount_minor_units,
                status,
                reserve_command_id,
                resolution_command_id,
                terminal_command_id,
                debit_operation_id,
                created_at,
                state_changed_at,
                version
            )
            VALUES
            (
                {row.Id},
                {row.CreditAccountId},
                {row.PrintSourceId},
                {row.JobUuid},
                {row.AmountMinorUnits},
                {row.Status},
                {row.ReserveCommandId},
                {row.ResolutionCommandId},
                {row.TerminalCommandId},
                {row.DebitOperationId},
                {row.CreatedAt},
                {row.StateChangedAt},
                {row.Version}
            )
            """);
    }

    private static ReservationRow CreateReservation(Guid accountId)
    {
        return new ReservationRow(
            Guid.NewGuid(),
            accountId,
            Guid.NewGuid(),
            $"urn:uuid:{Guid.NewGuid():D}",
            1_250,
            1,
            Guid.NewGuid(),
            null,
            null,
            null,
            TestTime,
            TestTime,
            1);
    }

    private static ReservationRow CreateInvalidReservation(
        ReservationRow row,
        string invalidShape)
    {
        return invalidShape switch
        {
            "empty-id" => row with { Id = Guid.Empty },
            "empty-account" => row with { CreditAccountId = Guid.Empty },
            "empty-source" => row with { PrintSourceId = Guid.Empty },
            "empty-reserve-command" => row with
            {
                ReserveCommandId = Guid.Empty
            },
            "empty-resolution-command" => row with
            {
                Status = 2,
                ResolutionCommandId = Guid.Empty
            },
            "empty-terminal-command" => row with
            {
                Status = 4,
                TerminalCommandId = Guid.Empty
            },
            "empty-debit-operation" => row with
            {
                Status = 3,
                TerminalCommandId = Guid.NewGuid(),
                DebitOperationId = Guid.Empty
            },
            "uppercase-job-uuid" => row with
            {
                JobUuid = row.JobUuid.ToUpperInvariant()
            },
            "nil-job-uuid" => row with
            {
                JobUuid =
                    "urn:uuid:00000000-0000-0000-0000-000000000000"
            },
            "zero-amount" => row with { AmountMinorUnits = 0 },
            "unknown-status" => row with { Status = 0 },
            "reversed-time" => row with
            {
                StateChangedAt = row.CreatedAt.AddTicks(-1)
            },
            "zero-version" => row with { Version = 0 },
            "reserved-with-terminal" => row with
            {
                TerminalCommandId = Guid.NewGuid()
            },
            "resolution-with-debit" => row with
            {
                Status = 2,
                ResolutionCommandId = Guid.NewGuid(),
                DebitOperationId = Guid.NewGuid()
            },
            "resolution-without-command" => row with
            {
                Status = 2
            },
            "resolution-with-terminal" => row with
            {
                Status = 2,
                ResolutionCommandId = Guid.NewGuid(),
                TerminalCommandId = Guid.NewGuid()
            },
            "captured-without-debit" => row with
            {
                Status = 3,
                TerminalCommandId = Guid.NewGuid()
            },
            "released-with-debit" => row with
            {
                Status = 4,
                TerminalCommandId = Guid.NewGuid(),
                DebitOperationId = Guid.NewGuid()
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(invalidShape))
        };
    }

    private static ReservationRow CreateReservationForScope(
        Guid accountId,
        string uniquenessScope)
    {
        var row = CreateReservation(accountId);

        return uniquenessScope switch
        {
            "print-job" or "reserve-command" => row,
            "resolution-command" => row with
            {
                Status = 2,
                ResolutionCommandId = Guid.NewGuid()
            },
            "terminal-command" => row with
            {
                Status = 4,
                TerminalCommandId = Guid.NewGuid()
            },
            "debit-operation" => row with
            {
                Status = 3,
                TerminalCommandId = Guid.NewGuid(),
                DebitOperationId = Guid.NewGuid()
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(uniquenessScope))
        };
    }

    private static ReservationRow CreateScopedDuplicate(
        ReservationRow original,
        string uniquenessScope,
        bool sameSource)
    {
        var duplicate = original with
        {
            Id = Guid.NewGuid(),
            PrintSourceId = sameSource
                ? original.PrintSourceId
                : Guid.NewGuid(),
            JobUuid = $"urn:uuid:{Guid.NewGuid():D}",
            ReserveCommandId = Guid.NewGuid(),
            ResolutionCommandId = original.ResolutionCommandId.HasValue
                ? Guid.NewGuid()
                : null,
            TerminalCommandId = original.TerminalCommandId.HasValue
                ? Guid.NewGuid()
                : null,
            DebitOperationId = original.DebitOperationId.HasValue
                ? Guid.NewGuid()
                : null
        };

        return uniquenessScope switch
        {
            "print-job" => duplicate with
            {
                JobUuid = original.JobUuid
            },
            "reserve-command" => duplicate with
            {
                ReserveCommandId = original.ReserveCommandId
            },
            "resolution-command" => duplicate with
            {
                ResolutionCommandId = original.ResolutionCommandId
            },
            "terminal-command" => duplicate with
            {
                TerminalCommandId = original.TerminalCommandId
            },
            "debit-operation" => duplicate with
            {
                DebitOperationId = original.DebitOperationId
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(uniquenessScope))
        };
    }

    private static string ExpectedUniqueConstraint(
        string uniquenessScope)
    {
        return uniquenessScope switch
        {
            "print-job" =>
                "uq_credits_print_reservations_print_job",
            "reserve-command" =>
                "uq_credits_print_reservations_reserve_command",
            "resolution-command" =>
                "uq_credits_print_reservations_resolution_command",
            "terminal-command" =>
                "uq_credits_print_reservations_terminal_command",
            "debit-operation" =>
                "uq_credits_print_reservations_debit_operation",
            _ => throw new ArgumentOutOfRangeException(
                nameof(uniquenessScope))
        };
    }

    private sealed record ReservationRow(
        Guid Id,
        Guid CreditAccountId,
        Guid PrintSourceId,
        string JobUuid,
        long AmountMinorUnits,
        int Status,
        Guid ReserveCommandId,
        Guid? ResolutionCommandId,
        Guid? TerminalCommandId,
        Guid? DebitOperationId,
        DateTimeOffset CreatedAt,
        DateTimeOffset StateChangedAt,
        long Version);
}
