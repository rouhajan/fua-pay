using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace FuaPay.DatabaseTests;

public sealed class SettlementReturnPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public SettlementReturnPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Migration_CreatesExpectedTableConstraintsAndIndexes()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        var tableExists = await dbContext.Database
            .SqlQuery<bool>(
                $"""
                SELECT to_regclass('payments.settlement_returns') IS NOT NULL
                    AS "Value"
                """)
            .SingleAsync();

        var constraints = await dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT constraint_name AS "Value"
                FROM information_schema.table_constraints
                WHERE table_schema = 'payments'
                  AND table_name = 'settlement_returns'
                """)
            .ToListAsync();

        var indexes = await dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'payments'
                  AND tablename = 'settlement_returns'
                """)
            .ToListAsync();

        Assert.True(tableExists);
        string[] expectedConstraints =
        [
            "pk_payments_settlement_returns",
            "fk_payments_settlement_returns_original_payment",
            "ck_payments_settlement_returns_id_not_empty",
            "ck_payments_settlement_returns_request_not_empty",
            "ck_payments_settlement_returns_customer_not_empty",
            "ck_payments_settlement_returns_admin_not_empty",
            "ck_payments_settlement_returns_kind_valid",
            "ck_payments_settlement_returns_original_not_empty",
            "ck_payments_settlement_returns_job_not_empty",
            "ck_payments_settlement_returns_source_consistent",
            "ck_payments_settlement_returns_amount_positive",
            "ck_payments_settlement_returns_currency_supported",
            "ck_payments_settlement_returns_reason_not_blank",
            "ck_payments_settlement_returns_state_valid",
            "ck_payments_settlement_returns_timestamps_ordered",
            "ck_payments_settlement_returns_state_consistent",
            "ck_payments_settlement_returns_version_positive"
        ];

        Assert.All(
            expectedConstraints,
            expected => Assert.Contains(expected, constraints));
        Assert.Contains(
            "uq_payments_settlement_returns_request",
            indexes);
        Assert.Contains(
            "uq_payments_settlement_returns_original_payment",
            indexes);
        Assert.Contains(
            "uq_payments_settlement_returns_job",
            indexes);
    }

    [Fact]
    public async Task Database_EnforcesRequestIdUniqueness()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        var repository = scope.ServiceProvider
            .GetRequiredService<ISettlementReturnRepository>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var requestId = Guid.NewGuid();

        try
        {
            await repository.AddAsync(CreateCreditJob(requestId: requestId));

            var exception = await Assert.ThrowsAsync<
                SettlementReturnRequestAlreadyExistsException>(
                    () => repository.AddAsync(
                        CreateCreditJob(requestId: requestId)));

            Assert.Equal(requestId, exception.RequestId);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Database_EnforcesOriginalPaymentUniqueness()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        var payment = await AddSucceededPaymentAsync(scope.ServiceProvider);
        var repository = scope.ServiceProvider
            .GetRequiredService<ISettlementReturnRepository>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            await repository.AddAsync(CreateCardTopUp(payment));

            var exception = await Assert.ThrowsAsync<
                SettlementReturnOriginalPaymentAlreadyExistsException>(
                    () => repository.AddAsync(CreateCardTopUp(payment)));

            Assert.Equal(payment.Id, exception.OriginalPaymentId);
        }
        finally
        {
            await transaction.RollbackAsync();
            await DeletePaymentsAsync(payment.Id);
        }
    }

    [Fact]
    public async Task Database_EnforcesJobIdUniqueness()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        var repository = scope.ServiceProvider
            .GetRequiredService<ISettlementReturnRepository>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var jobId = Guid.NewGuid();

        try
        {
            await repository.AddAsync(CreateCreditJob(jobId: jobId));

            var exception = await Assert.ThrowsAsync<
                SettlementReturnJobAlreadyExistsException>(
                    () => repository.AddAsync(
                        CreateCreditJob(jobId: jobId)));

            Assert.Equal(jobId, exception.JobId);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Database_AllowsMultipleNullsForSupportedSourceKinds()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var repository = scope.ServiceProvider
            .GetRequiredService<ISettlementReturnRepository>();
        var firstPayment =
            await AddSucceededPaymentAsync(scope.ServiceProvider);
        var secondPayment =
            await AddSucceededPaymentAsync(scope.ServiceProvider);

        try
        {
            var returns = new[]
            {
                CreateCreditJob(),
                CreateCreditJob(),
                CreateCardTopUp(firstPayment),
                CreateCardTopUp(secondPayment)
            };

            foreach (var settlementReturn in returns)
            {
                await repository.AddAsync(settlementReturn);
            }

            var count = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::integer AS "Value"
                    FROM payments.settlement_returns
                    WHERE id = ANY ({returns.Select(item => item.Id).ToArray()})
                    """)
                .SingleAsync();

            Assert.Equal(returns.Length, count);
        }
        finally
        {
            await transaction.RollbackAsync();
            await DeletePaymentsAsync(firstPayment.Id, secondPayment.Id);
        }
    }

    [Theory]
    [InlineData("source", "ck_payments_settlement_returns_source_consistent")]
    [InlineData("amount", "ck_payments_settlement_returns_amount_positive")]
    [InlineData("currency", "ck_payments_settlement_returns_currency_supported")]
    [InlineData("reason", "ck_payments_settlement_returns_reason_not_blank")]
    [InlineData("state", "ck_payments_settlement_returns_state_consistent")]
    public async Task Database_RejectsInvalidDurableShapes(
        string invalidField,
        string expectedConstraint)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            var row = CreateRawRow() with
            {
                OriginalPaymentId = invalidField == "source"
                    ? Guid.NewGuid()
                    : null,
                AmountMinorUnits = invalidField == "amount" ? 0 : 12_345,
                Currency = invalidField == "currency" ? "EUR" : "CZK",
                Reason = invalidField == "reason" ? "   " : "Reason",
                State = invalidField == "state" ? 2 : 1
            };

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => InsertRawAsync(dbContext, row));

            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal(expectedConstraint, exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Database_RestrictsDeletingOriginalPayment()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        var payment = await AddSucceededPaymentAsync(scope.ServiceProvider);
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        try
        {
            await scope.ServiceProvider
                .GetRequiredService<ISettlementReturnRepository>()
                .AddAsync(CreateCardTopUp(payment));

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM payments.payments
                    WHERE id = {payment.Id}
                    """));

            Assert.Equal(
                PostgresErrorCodes.RestrictViolation,
                exception.SqlState);
            Assert.Equal(
                "fk_payments_settlement_returns_original_payment",
                exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
            await DeletePaymentsAsync(payment.Id);
        }
    }

    [Fact]
    public async Task Registration_SameRequestReplayReturnsExisting()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var service = scope.ServiceProvider
            .GetRequiredService<SettlementReturnRegistrationService>();
        var original = CreateCreditJob();

        try
        {
            var created = await service.RegisterAsync(original);
            var replay = await service.RegisterAsync(
                CreateMatching(original));

            Assert.True(created.Created);
            Assert.False(replay.Created);
            Assert.Equal(original.Id, replay.SettlementReturn.Id);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Registration_ConflictingRequestReuseIsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();
        var service = scope.ServiceProvider
            .GetRequiredService<SettlementReturnRegistrationService>();
        var original = CreateCreditJob();

        try
        {
            await service.RegisterAsync(original);

            var exception = await Assert.ThrowsAsync<
                SettlementReturnRequestConflictException>(
                    () => service.RegisterAsync(
                        CreateMatching(
                            original,
                            reason: "Conflicting reason")));

            Assert.Equal(original.RequestId, exception.RequestId);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ConcurrentDifferentRequestsForOneSource_CreateOneReturn()
    {
        var sharedJobId = Guid.NewGuid();
        var first = CreateCreditJob(jobId: sharedJobId);
        var second = CreateCreditJob(jobId: sharedJobId);

        try
        {
            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstService = firstScope.ServiceProvider
                .GetRequiredService<SettlementReturnRegistrationService>();
            var secondService = secondScope.ServiceProvider
                .GetRequiredService<SettlementReturnRegistrationService>();

            var firstAttempt = CaptureAsync(
                () => firstService.RegisterAsync(first));
            var secondAttempt = CaptureAsync(
                () => secondService.RegisterAsync(second));
            var outcomes = await Task.WhenAll(
                firstAttempt,
                secondAttempt);

            Assert.Single(outcomes, outcome => outcome.Result is not null);
            var conflict = Assert.Single(
                outcomes,
                outcome =>
                    outcome.Exception is
                        SettlementReturnSourceConflictException);
            var created = Assert.Single(
                outcomes,
                outcome => outcome.Result is not null).Result!;
            var sourceConflict = Assert.IsType<
                SettlementReturnSourceConflictException>(
                    conflict.Exception);

            Assert.True(created.Created);
            Assert.Equal(
                created.SettlementReturn.Id,
                sourceConflict.ExistingSettlementReturnId);

            using var verifyScope = _factory.Services.CreateScope();
            var dbContext = verifyScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var storedCount = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::integer AS "Value"
                    FROM payments.settlement_returns
                    WHERE job_id = {sharedJobId}
                    """)
                .SingleAsync();

            Assert.Equal(1, storedCount);
        }
        finally
        {
            await DeleteSettlementReturnsAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task Repository_UsesOptimisticVersionConcurrency()
    {
        var settlementReturn = CreateCreditJob();

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<ISettlementReturnRepository>()
                    .AddAsync(settlementReturn);
            }

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var firstRepository = firstScope.ServiceProvider
                .GetRequiredService<ISettlementReturnRepository>();
            var secondRepository = secondScope.ServiceProvider
                .GetRequiredService<ISettlementReturnRepository>();
            var first = Assert.IsType<SettlementReturn>(
                await firstRepository.FindByIdAsync(settlementReturn.Id));
            var second = Assert.IsType<SettlementReturn>(
                await secondRepository.FindByIdAsync(settlementReturn.Id));

            first.Begin(RequestedAt.AddMinutes(1));
            second.Begin(RequestedAt.AddMinutes(2));
            await firstRepository.SaveAsync(first);

            await Assert.ThrowsAsync<SettlementReturnConcurrencyException>(
                () => secondRepository.SaveAsync(second));

            using var verifyScope = _factory.Services.CreateScope();
            var dbContext = verifyScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var version = await dbContext.Database
                .SqlQuery<long>(
                    $"""
                    SELECT version AS "Value"
                    FROM payments.settlement_returns
                    WHERE id = {settlementReturn.Id}
                    """)
                .SingleAsync();

            Assert.Equal(2, version);
        }
        finally
        {
            await DeleteSettlementReturnsAsync(settlementReturn.Id);
        }
    }

    private static async Task<RegistrationOutcome> CaptureAsync(
        Func<Task<SettlementReturnRegistrationResult>> action)
    {
        try
        {
            return new RegistrationOutcome(await action(), null);
        }
        catch (Exception exception)
        {
            return new RegistrationOutcome(null, exception);
        }
    }

    private async Task<Payment> AddSucceededPaymentAsync(
        IServiceProvider services)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(12_345),
            PaymentProvider.Development,
            RequestedAt,
            Guid.NewGuid());
        payment.MarkPending(
            $"DEV-RETURN-ORIGINAL-{Guid.NewGuid():N}",
            RequestedAt.AddMinutes(1));
        payment.Complete(RequestedAt.AddMinutes(2));

        await services
            .GetRequiredService<IPaymentRepository>()
            .AddAsync(payment);

        return payment;
    }

    private static SettlementReturn CreateCreditJob(
        Guid? requestId = null,
        Guid? jobId = null)
    {
        return new SettlementReturn(
            Guid.NewGuid(),
            requestId ?? Guid.NewGuid(),
            SettlementReturnKind.CreditJob,
            originalPaymentId: null,
            jobId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(12_345),
            "Administrative reason",
            RequestedAt);
    }

    private static SettlementReturn CreateCardTopUp(Payment payment)
    {
        return new SettlementReturn(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SettlementReturnKind.CardTopUp,
            payment.Id,
            jobId: null,
            payment.CustomerUserId,
            Guid.NewGuid(),
            payment.Amount,
            "Administrative reason",
            RequestedAt.AddMinutes(3));
    }

    private static SettlementReturn CreateMatching(
        SettlementReturn original,
        string? reason = null)
    {
        return new SettlementReturn(
            Guid.NewGuid(),
            original.RequestId,
            original.Kind,
            original.OriginalPaymentId,
            original.JobId,
            original.CustomerUserId,
            original.AdministratorUserId,
            original.Amount,
            reason ?? original.Reason,
            RequestedAt.AddSeconds(30));
    }

    private static RawSettlementReturnRow CreateRawRow()
    {
        return new RawSettlementReturnRow(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Kind: 2,
            OriginalPaymentId: null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            AmountMinorUnits: 12_345,
            Currency: "CZK",
            Reason: "Reason",
            State: 1,
            RequestedAt,
            StartedAt: null,
            RequestedAt,
            CompletedAt: null,
            Version: 1);
    }

    private static Task<int> InsertRawAsync(
        FuaPayDbContext dbContext,
        RawSettlementReturnRow row)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO payments.settlement_returns
            (
                id,
                request_id,
                kind,
                original_payment_id,
                job_id,
                customer_user_id,
                administrator_user_id,
                amount_minor_units,
                currency,
                reason,
                state,
                requested_at,
                started_at,
                updated_at,
                completed_at,
                version
            )
            VALUES
            (
                {row.Id},
                {row.RequestId},
                {row.Kind},
                {row.OriginalPaymentId},
                {row.JobId},
                {row.CustomerUserId},
                {row.AdministratorUserId},
                {row.AmountMinorUnits},
                {row.Currency},
                {row.Reason},
                {row.State},
                {row.RequestedAt},
                {row.StartedAt},
                {row.UpdatedAt},
                {row.CompletedAt},
                {row.Version}
            )
            """);
    }

    private async Task DeleteSettlementReturnsAsync(params Guid[] ids)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        foreach (var id in ids)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM payments.settlement_returns
                WHERE id = {id}
                """);
        }
    }

    private async Task DeletePaymentsAsync(params Guid[] ids)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        foreach (var id in ids)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM payments.payments
                WHERE id = {id}
                """);
        }
    }

    private sealed record RegistrationOutcome(
        SettlementReturnRegistrationResult? Result,
        Exception? Exception);

    private sealed record RawSettlementReturnRow(
        Guid Id,
        Guid RequestId,
        int Kind,
        Guid? OriginalPaymentId,
        Guid? JobId,
        Guid CustomerUserId,
        Guid AdministratorUserId,
        long AmountMinorUnits,
        string Currency,
        string Reason,
        int State,
        DateTimeOffset RequestedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt,
        long Version);
}
