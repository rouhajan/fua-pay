using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace FuaPay.DatabaseTests;

public sealed class SettlementReturnProviderAttemptPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public SettlementReturnProviderAttemptPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Migration_CreatesProtectedAttemptTable()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        var tableExists = await dbContext.Database
            .SqlQuery<bool>(
                $"""
                SELECT to_regclass(
                    'payments.settlement_return_provider_attempts')
                    IS NOT NULL AS "Value"
                """)
            .SingleAsync();
        var constraints = await dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT constraint_name AS "Value"
                FROM information_schema.table_constraints
                WHERE table_schema = 'payments'
                  AND table_name =
                      'settlement_return_provider_attempts'
                """)
            .ToListAsync();
        var indexes = await dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'payments'
                  AND tablename =
                      'settlement_return_provider_attempts'
                """)
            .ToListAsync();

        Assert.True(tableExists);
        string[] expectedConstraints =
        [
            "pk_payments_return_provider_attempts",
            "fk_payments_return_provider_attempts_settlement_return",
            "ck_payments_return_provider_attempts_id_not_empty",
            "ck_payments_return_provider_attempts_return_not_empty",
            "ck_payments_return_provider_attempts_provider_valid",
            "ck_payments_return_provider_attempts_operation_valid",
            "ck_payments_return_provider_attempts_reference_not_blank",
            "ck_payments_return_provider_attempts_state_valid",
            "ck_payments_return_provider_attempts_timestamps_ordered",
            "ck_payments_return_provider_attempts_state_consistent",
            "ck_payments_return_provider_attempts_diagnostic_consistent",
            "ck_payments_return_provider_attempts_version_positive"
        ];

        Assert.All(
            expectedConstraints,
            expected => Assert.Contains(expected, constraints));
        Assert.Contains(
            "uq_payments_return_provider_attempts_sequence",
            indexes);
        Assert.Contains(
            "ix_payments_return_provider_attempts_history",
            indexes);
    }

    [Fact]
    public async Task Repository_RoundTripsUncertainAttemptAcrossRestart()
    {
        var source = await AddSourceAsync();
        Guid attemptId = Guid.NewGuid();

        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<
                    SettlementReturnProviderAttemptService>();
                await service.CreateAsync(
                    new CreateSettlementReturnProviderAttemptCommand(
                        attemptId,
                        source.SettlementReturnId,
                        SettlementReturnProviderOperation.Refund));
                await service.BeginAsync(attemptId);
                await service.MarkUncertainAsync(
                    attemptId,
                    "connection timeout");
            }

            using var restartScope = _factory.Services.CreateScope();
            var repository = restartScope.ServiceProvider.GetRequiredService<
                ISettlementReturnProviderAttemptRepository>();
            var reloaded = Assert.IsType<
                SettlementReturnProviderAttempt>(
                    await repository.FindByIdAsync(attemptId));

            Assert.Equal(
                SettlementReturnProviderAttemptState.Uncertain,
                reloaded.State);
            Assert.True(reloaded.IsActive);
            Assert.Equal("connection timeout", reloaded.Diagnostic);
            Assert.Throws<
                InvalidSettlementReturnProviderAttemptStateTransitionException>(
                    () => reloaded.Begin(reloaded.UpdatedAt));
        }
        finally
        {
            await DeleteSourceAsync(source);
        }
    }

    [Fact]
    public async Task SequentialReverseThenRefund_PreservesBothRows()
    {
        var source = await AddSourceAsync();
        var reverseId = Guid.NewGuid();
        var refundId = Guid.NewGuid();

        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<
                    SettlementReturnProviderAttemptService>();
                await service.CreateAsync(
                    new CreateSettlementReturnProviderAttemptCommand(
                        reverseId,
                        source.SettlementReturnId,
                        SettlementReturnProviderOperation.Reverse));
                await service.BeginAsync(reverseId);
                await service.RejectAsync(
                    reverseId,
                    "payment settled before reverse");
                await service.CreateAsync(
                    new CreateSettlementReturnProviderAttemptCommand(
                        refundId,
                        source.SettlementReturnId,
                        SettlementReturnProviderOperation.Refund));
            }

            using var verifyScope = _factory.Services.CreateScope();
            var history = await verifyScope.ServiceProvider
                .GetRequiredService<
                    ISettlementReturnProviderAttemptRepository>()
                .ListBySettlementReturnIdAsync(
                    source.SettlementReturnId);
            var reverse = Assert.Single(
                history,
                item => item.Id == reverseId);
            var refund = Assert.Single(
                history,
                item => item.Id == refundId);

            Assert.Equal(
                SettlementReturnProviderOperation.Reverse,
                reverse.Operation);
            Assert.Equal(
                SettlementReturnProviderAttemptState.Rejected,
                reverse.State);
            Assert.Equal(
                SettlementReturnProviderOperation.Refund,
                refund.Operation);
            Assert.Equal(
                SettlementReturnProviderAttemptState.Prepared,
                refund.State);
        }
        finally
        {
            await DeleteSourceAsync(source);
        }
    }

    [Fact]
    public async Task ConfirmedAttempt_PreventsAnyLaterAttempt()
    {
        var source = await AddSourceAsync();
        var confirmedId = Guid.NewGuid();

        try
        {
            using var scope = _factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<
                SettlementReturnProviderAttemptService>();
            await service.CreateAsync(
                new CreateSettlementReturnProviderAttemptCommand(
                    confirmedId,
                    source.SettlementReturnId,
                    SettlementReturnProviderOperation.Reverse));
            await service.BeginAsync(confirmedId);
            await service.ConfirmAsync(confirmedId);

            await Assert.ThrowsAsync<
                SettlementReturnProviderAttemptNotAllowedException>(
                    () => scope.ServiceProvider.GetRequiredService<
                            ISettlementReturnProviderAttemptRepository>()
                        .AddAsync(
                            CreateAttempt(
                                Guid.NewGuid(),
                                source.SettlementReturnId,
                                SettlementReturnProviderOperation.Refund)));
        }
        finally
        {
            await DeleteSourceAsync(source);
        }
    }

    [Fact]
    public async Task Database_RejectsSecondActiveAttempt()
    {
        var source = await AddSourceAsync();

        try
        {
            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<
                ISettlementReturnProviderAttemptRepository>();
            await repository.AddAsync(
                CreateAttempt(
                    Guid.NewGuid(),
                    source.SettlementReturnId,
                    SettlementReturnProviderOperation.Reverse));

            var exception = await Assert.ThrowsAsync<
                SettlementReturnProviderAttemptAlreadyActiveException>(
                    () => repository.AddAsync(
                        CreateAttempt(
                            Guid.NewGuid(),
                            source.SettlementReturnId,
                            SettlementReturnProviderOperation.Refund)));

            Assert.Equal(
                source.SettlementReturnId,
                exception.SettlementReturnId);
        }
        finally
        {
            await DeleteSourceAsync(source);
        }
    }

    [Fact]
    public async Task ConcurrentCreation_AllowsExactlyOneActiveAttempt()
    {
        var source = await AddSourceAsync();

        try
        {
            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstService = firstScope.ServiceProvider.GetRequiredService<
                SettlementReturnProviderAttemptService>();
            var secondService = secondScope.ServiceProvider.GetRequiredService<
                SettlementReturnProviderAttemptService>();
            var first = CaptureCreationAsync(
                () => firstService.CreateAsync(
                    new CreateSettlementReturnProviderAttemptCommand(
                        Guid.NewGuid(),
                        source.SettlementReturnId,
                        SettlementReturnProviderOperation.Reverse)));
            var second = CaptureCreationAsync(
                () => secondService.CreateAsync(
                    new CreateSettlementReturnProviderAttemptCommand(
                        Guid.NewGuid(),
                        source.SettlementReturnId,
                        SettlementReturnProviderOperation.Refund)));

            var outcomes = await Task.WhenAll(first, second);

            Assert.Single(outcomes, item => item.Result is not null);
            Assert.Single(
                outcomes,
                item => item.Exception is
                    SettlementReturnProviderAttemptAlreadyActiveException);

            using var verifyScope = _factory.Services.CreateScope();
            var count = await verifyScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>()
                .Database.SqlQuery<int>(
                    $"""
                    SELECT count(*)::integer AS "Value"
                    FROM payments.settlement_return_provider_attempts
                    WHERE settlement_return_id =
                        {source.SettlementReturnId}
                      AND state IN (1, 2, 5)
                    """)
                .SingleAsync();

            Assert.Equal(1, count);
        }
        finally
        {
            await DeleteSourceAsync(source);
        }
    }

    [Fact]
    public async Task ConcurrentSameAttemptId_ReplaysOneDurableRow()
    {
        var source = await AddSourceAsync();
        var command = new CreateSettlementReturnProviderAttemptCommand(
            Guid.NewGuid(),
            source.SettlementReturnId,
            SettlementReturnProviderOperation.Refund);

        try
        {
            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstService = firstScope.ServiceProvider.GetRequiredService<
                SettlementReturnProviderAttemptService>();
            var secondService = secondScope.ServiceProvider.GetRequiredService<
                SettlementReturnProviderAttemptService>();

            var results = await Task.WhenAll(
                firstService.CreateAsync(command),
                secondService.CreateAsync(command));

            Assert.Single(results, item => item.Created);
            Assert.Single(results, item => !item.Created);
            Assert.All(
                results,
                item => Assert.Equal(command.AttemptId, item.Attempt.Id));

            using var verifyScope = _factory.Services.CreateScope();
            var count = await verifyScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>()
                .Database.SqlQuery<int>(
                    $"""
                    SELECT count(*)::integer AS "Value"
                    FROM payments.settlement_return_provider_attempts
                    WHERE id = {command.AttemptId}
                    """)
                .SingleAsync();

            Assert.Equal(1, count);
        }
        finally
        {
            await DeleteSourceAsync(source);
        }
    }

    [Fact]
    public async Task Repository_UsesOptimisticConcurrency()
    {
        var source = await AddSourceAsync();
        var attempt = CreateAttempt(
            Guid.NewGuid(),
            source.SettlementReturnId,
            SettlementReturnProviderOperation.Refund);

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider.GetRequiredService<
                    ISettlementReturnProviderAttemptRepository>()
                    .AddAsync(attempt);
            }

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstRepository = firstScope.ServiceProvider
                .GetRequiredService<
                    ISettlementReturnProviderAttemptRepository>();
            var secondRepository = secondScope.ServiceProvider
                .GetRequiredService<
                    ISettlementReturnProviderAttemptRepository>();
            var first = Assert.IsType<SettlementReturnProviderAttempt>(
                await firstRepository.FindByIdAsync(attempt.Id));
            var second = Assert.IsType<SettlementReturnProviderAttempt>(
                await secondRepository.FindByIdAsync(attempt.Id));
            var changedAt = DateTimeOffset.UtcNow.AddMinutes(1);

            first.Begin(changedAt);
            second.Reject("preflight rejection", changedAt);
            await firstRepository.SaveAsync(first);

            await Assert.ThrowsAsync<
                SettlementReturnProviderAttemptConcurrencyException>(
                    () => secondRepository.SaveAsync(second));
        }
        finally
        {
            await DeleteSourceAsync(source);
        }
    }

    [Theory]
    [InlineData("operation", "ck_payments_return_provider_attempts_operation_valid")]
    [InlineData("state", "ck_payments_return_provider_attempts_state_consistent")]
    [InlineData("diagnostic", "ck_payments_return_provider_attempts_diagnostic_consistent")]
    [InlineData("version", "ck_payments_return_provider_attempts_version_positive")]
    public async Task Database_RejectsInvalidDurableShapes(
        string invalidField,
        string expectedConstraint)
    {
        var source = await AddSourceAsync();
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var state = invalidField switch
            {
                "state" => 2,
                "diagnostic" => 5,
                _ => 1
            };
            DateTimeOffset? startedAt = invalidField == "diagnostic"
                ? BaseTime.AddMinutes(1)
                : null;
            var updatedAt = startedAt ?? BaseTime;

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO payments.settlement_return_provider_attempts
                    (
                        id, settlement_return_id, provider, operation,
                        provider_reference, state, diagnostic, created_at,
                        updated_at, started_at, finished_at, version
                    )
                    VALUES
                    (
                        {Guid.NewGuid()}, {source.SettlementReturnId}, 1,
                        {(invalidField == "operation" ? 0 : 2)},
                        {"provider-reference"}, {state}, {null},
                        {BaseTime}, {updatedAt}, {startedAt}, {null},
                        {(invalidField == "version" ? 0 : 1)}
                    )
                    """));

            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal(expectedConstraint, exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
            await DeleteSourceAsync(source);
        }
    }

    private async Task<SourceIds> AddSourceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(12_345),
            PaymentProvider.Development,
            BaseTime,
            Guid.NewGuid());
        payment.MarkPending(
            $"DEV-RETURN-ATTEMPT-{Guid.NewGuid():N}",
            BaseTime.AddMinutes(1));
        payment.Complete(BaseTime.AddMinutes(2));
        await scope.ServiceProvider
            .GetRequiredService<IPaymentRepository>()
            .AddAsync(payment);

        var settlementReturn = new SettlementReturn(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SettlementReturnKind.CardTopUp,
            payment.Id,
            jobId: null,
            payment.CustomerUserId,
            Guid.NewGuid(),
            payment.Amount,
            "Administrative reason",
            BaseTime.AddMinutes(3));
        await scope.ServiceProvider
            .GetRequiredService<ISettlementReturnRepository>()
            .AddAsync(settlementReturn);

        return new SourceIds(payment.Id, settlementReturn.Id);
    }

    private static SettlementReturnProviderAttempt CreateAttempt(
        Guid attemptId,
        Guid settlementReturnId,
        SettlementReturnProviderOperation operation) =>
        new(
            attemptId,
            settlementReturnId,
            PaymentProvider.Development,
            operation,
            $"DEV-ATTEMPT-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow);

    private static async Task<CreationOutcome> CaptureCreationAsync(
        Func<Task<SettlementReturnProviderAttemptCreationResult>> action)
    {
        try
        {
            return new CreationOutcome(await action(), null);
        }
        catch (Exception exception)
        {
            return new CreationOutcome(null, exception);
        }
    }

    private async Task DeleteSourceAsync(SourceIds source)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.settlement_return_provider_attempts
            WHERE settlement_return_id = {source.SettlementReturnId}
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.settlement_returns
            WHERE id = {source.SettlementReturnId}
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE id = {source.PaymentId}
            """);
    }

    private sealed record SourceIds(
        Guid PaymentId,
        Guid SettlementReturnId);

    private sealed record CreationOutcome(
        SettlementReturnProviderAttemptCreationResult? Result,
        Exception? Exception);
}
