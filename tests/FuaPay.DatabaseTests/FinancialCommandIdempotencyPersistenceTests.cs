using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FuaPay.DatabaseTests;

public sealed class FinancialCommandIdempotencyPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FinancialCommandIdempotencyPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PaymentCreationRequest_ConcurrentRetryPersistsOnePaymentAndAudit()
    {
        var creationRequestId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        try
        {
            var outcomes = await Task.WhenAll(
                CreateTopUpAsync(creationRequestId, customerUserId),
                CreateTopUpAsync(creationRequestId, customerUserId));

            Assert.Equal(outcomes[0].Id, outcomes[1].Id);

            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider
                .GetRequiredService<IPaymentRepository>();
            var persisted = Assert.IsType<Payment>(
                await repository.FindByCreationRequestIdAsync(
                    creationRequestId));

            Assert.Equal(outcomes[0].Id, persisted.Id);

            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var paymentCount = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM payments.payments
                    WHERE creation_request_id = {creationRequestId}
                    """)
                .SingleAsync();
            var auditCount = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'payment.created'
                      AND entity_id = {persisted.Id.ToString()}
                    """)
                .SingleAsync();

            Assert.Equal(1, paymentCount);
            Assert.Equal(1, auditCount);
        }
        finally
        {
            await DeleteTopUpAsync(creationRequestId);
        }
    }

    [Fact]
    public async Task PaymentCreationRequest_TimeoutBeforeCommitThenRetryPersistsOnce()
    {
        var creationRequestId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        using var failureFactory =
            CreatePaymentRepositoryFailureFactory(throwAfterWrite: false);

        try
        {
            using (var failureScope = failureFactory.Services.CreateScope())
            {
                var service = failureScope.ServiceProvider
                    .GetRequiredService<PaymentCreationService>();

                var exception = await Assert.ThrowsAsync<TimeoutException>(
                    () => service.CreateCreditTopUpAsync(
                        creationRequestId,
                        customerUserId,
                        new Money(50_000)));

                Assert.Equal(
                    ThrowingPaymentRepository.FailureBeforeWriteMessage,
                    exception.Message);
            }

            using (var verificationScope = _factory.Services.CreateScope())
            {
                var dbContext = verificationScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>();

                var paymentCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM payments.payments
                        WHERE creation_request_id = {creationRequestId}
                        """)
                    .SingleAsync();
                var auditCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM audit.events
                        WHERE action = 'payment.created'
                          AND actor_user_id = {customerUserId}
                        """)
                    .SingleAsync();

                Assert.Equal(0, paymentCount);
                Assert.Equal(0, auditCount);
            }

            var retry = await CreateTopUpAsync(
                creationRequestId,
                customerUserId);

            using var finalScope = _factory.Services.CreateScope();
            var finalDbContext = finalScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            var finalPaymentCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM payments.payments
                    WHERE creation_request_id = {creationRequestId}
                    """)
                .SingleAsync();
            var finalAuditCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'payment.created'
                      AND entity_id = {retry.Id.ToString()}
                    """)
                .SingleAsync();

            Assert.Equal(1, finalPaymentCount);
            Assert.Equal(1, finalAuditCount);
        }
        finally
        {
            await DeleteTopUpAsync(creationRequestId);
        }
    }

    [Fact]
    public async Task PaymentCreationRequest_TimeoutAfterCommitThenRetryReturnsOriginalPayment()
    {
        var creationRequestId = Guid.NewGuid();
        var customerUserId = Guid.NewGuid();

        using var failureFactory =
            CreatePaymentRepositoryFailureFactory(throwAfterWrite: true);

        try
        {
            using (var failureScope = failureFactory.Services.CreateScope())
            {
                var service = failureScope.ServiceProvider
                    .GetRequiredService<PaymentCreationService>();

                var exception = await Assert.ThrowsAsync<TimeoutException>(
                    () => service.CreateCreditTopUpAsync(
                        creationRequestId,
                        customerUserId,
                        new Money(50_000)));

                Assert.Equal(
                    ThrowingPaymentRepository.FailureAfterWriteMessage,
                    exception.Message);
            }

            Payment persisted;

            using (var verificationScope = _factory.Services.CreateScope())
            {
                var repository = verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>();
                persisted = Assert.IsType<Payment>(
                    await repository.FindByCreationRequestIdAsync(
                        creationRequestId));

                var dbContext = verificationScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>();
                var paymentCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM payments.payments
                        WHERE creation_request_id = {creationRequestId}
                        """)
                    .SingleAsync();
                var auditCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM audit.events
                        WHERE action = 'payment.created'
                          AND entity_id = {persisted.Id.ToString()}
                        """)
                    .SingleAsync();

                Assert.Equal(1, paymentCount);
                Assert.Equal(1, auditCount);
            }

            var retry = await CreateTopUpAsync(
                creationRequestId,
                customerUserId);

            Assert.Equal(persisted.Id, retry.Id);

            using var finalScope = _factory.Services.CreateScope();
            var finalDbContext = finalScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var finalPaymentCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM payments.payments
                    WHERE creation_request_id = {creationRequestId}
                    """)
                .SingleAsync();
            var finalAuditCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'payment.created'
                      AND entity_id = {persisted.Id.ToString()}
                    """)
                .SingleAsync();

            Assert.Equal(1, finalPaymentCount);
            Assert.Equal(1, finalAuditCount);
        }
        finally
        {
            await DeleteTopUpAsync(creationRequestId);
        }
    }

    [Fact]
    public async Task CreditAdjustmentCommand_ConcurrentRetryHasOneMovementAndOneAudit()
    {
        var command = new CreditAdjustmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(2_500),
            "PostgreSQL concurrency test");

        try
        {
            var results = await Task.WhenAll(
                AdjustAsync(command),
                AdjustAsync(command));

            Assert.Equal(results[0], results[1]);

            using var scope = _factory.Services.CreateScope();
            var accounts = scope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>();
            var account = Assert.IsType<CreditAccount>(
                await accounts.FindByOwnerIdAsync(
                    command.OwnerId,
                    CancellationToken.None));

            Assert.Single(account.Movements);
            Assert.Equal(command.CommandId, account.Movements[0].OperationId);

            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var auditCount = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.adjusted'
                      AND entity_id = {command.OwnerId.ToString()}
                    """)
                .SingleAsync();
            var commandCount = await dbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM credits.adjustment_commands
                    WHERE command_id = {command.CommandId}
                    """)
                .SingleAsync();

            Assert.Equal(1, auditCount);
            Assert.Equal(1, commandCount);
        }
        finally
        {
            await DeleteCreditAdjustmentAsync(command);
        }
    }

    [Fact]
    public async Task CreditAdjustmentCommand_TimeoutBeforeCommitRollsBackThenRetryPersistsOnce()
    {
        var command = new CreditAdjustmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(2_500),
            "PostgreSQL timeout-before-commit test");

        using var failureFactory =
            CreateCreditRepositoryFailureFactory(command.OwnerId);

        try
        {
            using (var failureScope = failureFactory.Services.CreateScope())
            {
                var service = failureScope.ServiceProvider
                    .GetRequiredService<CreditAdministrationService>();

                var exception = await Assert.ThrowsAsync<TimeoutException>(
                    () => service.AdjustAsync(command));

                Assert.Equal(
                    ThrowAfterWriteCreditAccountRepository.FailureMessage,
                    exception.Message);
            }

            using (var verificationScope = _factory.Services.CreateScope())
            {
                var dbContext = verificationScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>();

                var commandCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM credits.adjustment_commands
                        WHERE command_id = {command.CommandId}
                        """)
                    .SingleAsync();
                var auditCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM audit.events
                        WHERE action = 'credit.adjusted'
                          AND entity_id = {command.OwnerId.ToString()}
                        """)
                    .SingleAsync();
                var movementCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM credits.movements
                        WHERE operation_id = {command.CommandId}
                        """)
                    .SingleAsync();
                var accountCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM credits.accounts
                        WHERE owner_id = {command.OwnerId}
                        """)
                    .SingleAsync();

                Assert.Equal(0, commandCount);
                Assert.Equal(0, auditCount);
                Assert.Equal(0, movementCount);
                Assert.Equal(0, accountCount);
            }

            var retry = await AdjustAsync(command);

            Assert.Equal(command.CommandId, retry.CommandId);

            using var finalScope = _factory.Services.CreateScope();
            var accounts = finalScope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>();
            var account = Assert.IsType<CreditAccount>(
                await accounts.FindByOwnerIdAsync(
                    command.OwnerId,
                    CancellationToken.None));

            Assert.Single(account.Movements);
            Assert.Equal(command.CommandId, account.Movements[0].OperationId);

            var finalDbContext = finalScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var finalCommandCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM credits.adjustment_commands
                    WHERE command_id = {command.CommandId}
                    """)
                .SingleAsync();
            var finalAuditCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.adjusted'
                      AND entity_id = {command.OwnerId.ToString()}
                    """)
                .SingleAsync();
            var finalMovementCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM credits.movements
                    WHERE operation_id = {command.CommandId}
                    """)
                .SingleAsync();

            Assert.Equal(1, finalCommandCount);
            Assert.Equal(1, finalAuditCount);
            Assert.Equal(1, finalMovementCount);
        }
        finally
        {
            await DeleteCreditAdjustmentAsync(command);
        }
    }

    [Fact]
    public async Task CreditAdjustmentCommand_TimeoutAfterCommitThenRetryReturnsOriginalResult()
    {
        var command = new CreditAdjustmentCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(2_500),
            "PostgreSQL timeout-after-commit test");

        using var failureFactory =
            CreateTransactionFailureAfterCommitFactory();

        try
        {
            using (var failureScope = failureFactory.Services.CreateScope())
            {
                var service = failureScope.ServiceProvider
                    .GetRequiredService<CreditAdministrationService>();

                var exception = await Assert.ThrowsAsync<TimeoutException>(
                    () => service.AdjustAsync(command));

                Assert.Equal(
                    ThrowAfterCommitApplicationTransaction.FailureMessage,
                    exception.Message);
            }

            CreditAdjustmentResult persistedResult;

            using (var verificationScope = _factory.Services.CreateScope())
            {
                var commandRepository = verificationScope.ServiceProvider
                    .GetRequiredService<ICreditAdjustmentCommandRepository>();
                var persisted = Assert.IsType<PersistedCreditAdjustmentCommand>(
                    await commandRepository.FindAsync(command.CommandId));
                persistedResult = persisted.Result;

                var dbContext = verificationScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>();
                var commandCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM credits.adjustment_commands
                        WHERE command_id = {command.CommandId}
                        """)
                    .SingleAsync();
                var auditCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM audit.events
                        WHERE action = 'credit.adjusted'
                          AND entity_id = {command.OwnerId.ToString()}
                        """)
                    .SingleAsync();
                var movementCount = await dbContext.Database
                    .SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value"
                        FROM credits.movements
                        WHERE operation_id = {command.CommandId}
                        """)
                    .SingleAsync();

                Assert.Equal(1, commandCount);
                Assert.Equal(1, auditCount);
                Assert.Equal(1, movementCount);
            }

            var retry = await AdjustAsync(command);

            Assert.Equal(persistedResult, retry);

            using var finalScope = _factory.Services.CreateScope();
            var finalDbContext = finalScope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();
            var finalCommandCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM credits.adjustment_commands
                    WHERE command_id = {command.CommandId}
                    """)
                .SingleAsync();
            var finalAuditCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM audit.events
                    WHERE action = 'credit.adjusted'
                      AND entity_id = {command.OwnerId.ToString()}
                    """)
                .SingleAsync();
            var finalMovementCount = await finalDbContext.Database
                .SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value"
                    FROM credits.movements
                    WHERE operation_id = {command.CommandId}
                    """)
                .SingleAsync();

            Assert.Equal(1, finalCommandCount);
            Assert.Equal(1, finalAuditCount);
            Assert.Equal(1, finalMovementCount);
        }
        finally
        {
            await DeleteCreditAdjustmentAsync(command);
        }
    }

    private WebApplicationFactory<Program>
        CreatePaymentRepositoryFailureFactory(bool throwAfterWrite)
    {
        return _factory.WithWebHostBuilder(
            builder =>
                builder.ConfigureServices(
                    services =>
                    {
                        var originalDescriptor =
                            services.Last(
                                descriptor =>
                                    descriptor.ServiceType ==
                                    typeof(IPaymentRepository));
                        var implementationType =
                            originalDescriptor.ImplementationType ??
                            throw new InvalidOperationException(
                                "IPaymentRepository must be registered by implementation type.");

                        services.RemoveAll<IPaymentRepository>();
                        services.AddScoped<IPaymentRepository>(
                            serviceProvider =>
                                new ThrowingPaymentRepository(
                                    (IPaymentRepository)
                                        ActivatorUtilities.CreateInstance(
                                            serviceProvider,
                                            implementationType),
                                    throwAfterWrite));
                    }));
    }

    private WebApplicationFactory<Program>
        CreateCreditRepositoryFailureFactory(Guid targetOwnerId)
    {
        return _factory.WithWebHostBuilder(
            builder =>
                builder.ConfigureServices(
                    services =>
                    {
                        var originalDescriptor =
                            services.Last(
                                descriptor =>
                                    descriptor.ServiceType ==
                                    typeof(ICreditAccountRepository));
                        var implementationType =
                            originalDescriptor.ImplementationType ??
                            throw new InvalidOperationException(
                                "ICreditAccountRepository must be registered by implementation type.");

                        services.RemoveAll<ICreditAccountRepository>();
                        services.AddScoped<ICreditAccountRepository>(
                            serviceProvider =>
                                new ThrowAfterWriteCreditAccountRepository(
                                    (ICreditAccountRepository)
                                        ActivatorUtilities.CreateInstance(
                                            serviceProvider,
                                            implementationType),
                                    targetOwnerId));
                    }));
    }

    private WebApplicationFactory<Program>
        CreateTransactionFailureAfterCommitFactory()
    {
        return _factory.WithWebHostBuilder(
            builder =>
                builder.ConfigureServices(
                    services =>
                    {
                        var originalDescriptor =
                            services.Last(
                                descriptor =>
                                    descriptor.ServiceType ==
                                    typeof(IApplicationTransaction));
                        var implementationType =
                            originalDescriptor.ImplementationType ??
                            throw new InvalidOperationException(
                                "IApplicationTransaction must be registered by implementation type.");

                        services.RemoveAll<IApplicationTransaction>();
                        services.AddScoped<IApplicationTransaction>(
                            serviceProvider =>
                                new ThrowAfterCommitApplicationTransaction(
                                    (IApplicationTransaction)
                                        ActivatorUtilities.CreateInstance(
                                            serviceProvider,
                                            implementationType),
                                    serviceProvider
                                        .GetRequiredService<FuaPayDbContext>()));
                    }));
    }

    private async Task<Payment> CreateTopUpAsync(
        Guid creationRequestId,
        Guid customerUserId)
    {
        using var scope = _factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<PaymentCreationService>()
            .CreateCreditTopUpAsync(
                creationRequestId,
                customerUserId,
                new Money(50_000));
    }

    private async Task<CreditAdjustmentResult> AdjustAsync(
        CreditAdjustmentCommand command)
    {
        using var scope = _factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<CreditAdministrationService>()
            .AdjustAsync(command);
    }

    private async Task DeleteTopUpAsync(Guid creationRequestId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE entity_type = 'payment'
              AND entity_id IN
              (
                  SELECT id::text
                  FROM payments.payments
                  WHERE creation_request_id = {creationRequestId}
              )
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE creation_request_id = {creationRequestId}
            """);
    }

    private async Task DeleteCreditAdjustmentAsync(
        CreditAdjustmentCommand command)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE action = 'credit.adjusted'
              AND entity_id = {command.OwnerId.ToString()}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.adjustment_commands
            WHERE command_id = {command.CommandId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.movements
            WHERE operation_id = {command.CommandId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.accounts
            WHERE owner_id = {command.OwnerId}
            """);

        await transaction.CommitAsync();
    }

    private sealed class ThrowingPaymentRepository : IPaymentRepository
    {
        public const string FailureBeforeWriteMessage =
            "Injected timeout before payment persistence.";
        public const string FailureAfterWriteMessage =
            "Injected timeout after payment persistence.";

        private readonly IPaymentRepository _inner;
        private readonly bool _throwAfterWrite;

        public ThrowingPaymentRepository(
            IPaymentRepository inner,
            bool throwAfterWrite)
        {
            _inner = inner;
            _throwAfterWrite = throwAfterWrite;
        }

        public Task<Payment?> FindByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindByIdAsync(
                paymentId,
                cancellationToken);
        }

        public Task<Payment?> FindBlockingForJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindBlockingForJobAsync(
                jobId,
                cancellationToken);
        }

        public Task<Payment?> FindByProviderReferenceAsync(
            PaymentProvider provider,
            string providerReference,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindByProviderReferenceAsync(
                provider,
                providerReference,
                cancellationToken);
        }

        public Task<Payment?> FindByCreationRequestIdAsync(
            Guid creationRequestId,
            CancellationToken cancellationToken = default)
        {
            return _inner.FindByCreationRequestIdAsync(
                creationRequestId,
                cancellationToken);
        }

        public async Task AddAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            if (!_throwAfterWrite)
            {
                throw new TimeoutException(
                    FailureBeforeWriteMessage);
            }

            await _inner.AddAsync(
                payment,
                cancellationToken);

            throw new TimeoutException(
                FailureAfterWriteMessage);
        }

        public async Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default)
        {
            if (!_throwAfterWrite)
            {
                throw new TimeoutException(
                    FailureBeforeWriteMessage);
            }

            await _inner.AddPreparedAsync(
                payment,
                initiation,
                cancellationToken);

            throw new TimeoutException(
                FailureAfterWriteMessage);
        }

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            return _inner.SaveAsync(
                payment,
                cancellationToken);
        }
    }

    private sealed class ThrowAfterWriteCreditAccountRepository :
        ICreditAccountRepository
    {
        public const string FailureMessage =
            "Injected timeout after credit persistence before commit.";

        private readonly ICreditAccountRepository _inner;
        private readonly Guid _targetOwnerId;

        public ThrowAfterWriteCreditAccountRepository(
            ICreditAccountRepository inner,
            Guid targetOwnerId)
        {
            _inner = inner;
            _targetOwnerId = targetOwnerId;
        }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            return _inner.FindByOwnerIdAsync(
                ownerId,
                cancellationToken);
        }

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            return _inner.FindByOwnerIdForUpdateAsync(
                ownerId,
                cancellationToken);
        }

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            return _inner.LockOwnerForAccountCreationAsync(
                ownerId,
                cancellationToken);
        }

        public async Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            await _inner.AddAsync(
                account,
                cancellationToken);

            if (account.OwnerId == _targetOwnerId)
            {
                throw new TimeoutException(
                    FailureMessage);
            }
        }

        public async Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            await _inner.SaveAsync(
                account,
                cancellationToken);

            if (account.OwnerId == _targetOwnerId)
            {
                throw new TimeoutException(
                    FailureMessage);
            }
        }
    }

    private sealed class ThrowAfterCommitApplicationTransaction :
        IApplicationTransaction
    {
        public const string FailureMessage =
            "Injected timeout after transaction commit.";

        private readonly IApplicationTransaction _inner;
        private readonly FuaPayDbContext _dbContext;

        public ThrowAfterCommitApplicationTransaction(
            IApplicationTransaction inner,
            FuaPayDbContext dbContext)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(dbContext);

            _inner = inner;
            _dbContext = dbContext;
        }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var ownsCommitBoundary =
                _dbContext.Database.CurrentTransaction is null;

            var result = await _inner.ExecuteAsync(
                operation,
                cancellationToken);

            if (!ownsCommitBoundary)
            {
                return result;
            }

            throw new TimeoutException(
                FailureMessage);
        }
    }
}
