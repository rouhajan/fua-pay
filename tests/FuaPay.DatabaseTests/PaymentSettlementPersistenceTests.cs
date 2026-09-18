using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Audit.Application;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Notifications.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PaymentSettlementPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentSettlementPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompleteAsync_DevelopmentTopUpCommitsWithoutDocumentOrNumber()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-ATOMIC-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            var counterBefore = await GetFinancialDocumentCounterTotalAsync();
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();

                var confirmation = CreateConfirmation(payment);

                Assert.True(
                    await service.CompleteAsync(confirmation));
                Assert.False(
                    await service.CompleteAsync(confirmation));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = Assert.IsType<CreditAccount>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        customerUserId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Succeeded,
                persistedPayment.Status);
            Assert.Equal(payment.Amount, account.Balance);

            var movement = Assert.Single(account.Movements);
            Assert.Equal(payment.Id, movement.OperationId);
            Assert.Equal(CreditMovementType.Credit, movement.Type);
            Assert.Equal(payment.Amount, movement.Amount);

            var audit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            var auditItem = Assert.Single(
                audit.Items,
                item => item.Action == "payment.succeeded");
            Assert.Equal(
                "payment-provider",
                auditItem.ActorProcessName);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            var notification = Assert.Single(
                notifications,
                item =>
                    item.RecipientUserId == customerUserId &&
                    item.Type == "payment.succeeded");

            Assert.Contains(
                payment.ProviderReference!,
                notification.Body,
                StringComparison.Ordinal);

            Assert.Null(await FindDocumentAsync(payment.Id));
            Assert.Equal(
                counterBefore,
                await GetFinancialDocumentCounterTotalAsync());
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_ConcurrentDuplicateTopUpSettlesExactlyOnce()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-CONCURRENT-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            await AddPaymentAsync(payment);

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();

            var confirmation = CreateConfirmation(payment);
            var firstTask = firstScope.ServiceProvider
                .GetRequiredService<IPaymentSettlementService>()
                .CompleteAsync(confirmation);
            var secondTask = secondScope.ServiceProvider
                .GetRequiredService<IPaymentSettlementService>()
                .CompleteAsync(confirmation);

            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.Single(results, result => result);
            Assert.Single(results, result => !result);

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = Assert.IsType<CreditAccount>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        customerUserId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Succeeded,
                persistedPayment.Status);
            Assert.Equal(payment.Amount, account.Balance);
            Assert.Single(account.Movements);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_RejectsMismatchedAmountWithoutEffects()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-MISMATCH-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();
                var mismatched = new VerifiedPaymentConfirmation(
                    payment.Provider,
                    payment.ProviderReference!,
                    new Money(payment.Amount.MinorUnits + 1));

                await Assert.ThrowsAsync<
                    PaymentConfirmationMismatchException>(
                    () => service.CompleteAsync(mismatched));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = await verificationScope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdAsync(
                    customerUserId,
                    CancellationToken.None);

            Assert.Equal(
                PaymentStatus.Pending,
                persistedPayment.Status);
            Assert.Null(account);

            var audit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Empty(audit.Items);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.DoesNotContain(
                notifications,
                item => item.RecipientUserId == customerUserId);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenFinalSaveFails_RollsBackTopUp()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingTopUp(
            customerUserId,
            $"DEV-ROLLBACK-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var service = CreateFailingSettlementService(
                    services,
                    TestTime.AddMinutes(30));

                await Assert.ThrowsAsync<TestPaymentSaveException>(
                    () => service.CompleteAsync(
                        CreateConfirmation(payment)));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var account = await verificationScope.ServiceProvider
                .GetRequiredService<ICreditAccountRepository>()
                .FindByOwnerIdAsync(
                    customerUserId,
                    CancellationToken.None);

            Assert.Equal(
                PaymentStatus.Pending,
                persistedPayment.Status);
            Assert.Null(account);

            var audit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Empty(audit.Items);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.DoesNotContain(
                notifications,
                item => item.RecipientUserId == customerUserId);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_WhenFinalSaveFails_RollsBackJob()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var payment = CreatePendingJobPayment(
            customerUserId,
            jobId,
            $"DEV-ROLLBACK-JOB-{Guid.NewGuid():N}");

        try
        {
            await AddPublishedJobAsync(
                customerUserId,
                jobId,
                payment.Amount);
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = CreateFailingSettlementService(
                    scope.ServiceProvider,
                    TestTime.AddMinutes(30));

                await Assert.ThrowsAsync<TestPaymentSaveException>(
                    () => service.CompleteAsync(
                        CreateConfirmation(payment)));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var persistedJob = Assert.IsType<Job>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(
                        jobId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Pending,
                persistedPayment.Status);
            Assert.Equal(
                JobPaymentStatus.Unpaid,
                persistedJob.PaymentStatus);
            Assert.Null(persistedJob.SettlementType);
            Assert.Null(persistedJob.SettlementReferenceId);
            Assert.Null(persistedJob.SettledAt);

            var paymentAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Empty(paymentAudit.Items);

            var jobAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                jobId);
            Assert.Empty(jobAudit.Items);

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.DoesNotContain(
                notifications,
                item => item.RecipientUserId == customerUserId);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId);
        }
    }

    [Fact]
    public async Task CompleteAsync_DevelopmentJobCommitsWithoutDocumentOrNumber()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var payment = CreatePendingJobPayment(
            customerUserId,
            jobId,
            $"DEV-ATOMIC-JOB-{Guid.NewGuid():N}");

        try
        {
            var counterBefore = await GetFinancialDocumentCounterTotalAsync();
            await AddPublishedJobAsync(
                customerUserId,
                jobId,
                payment.Amount);
            await AddPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();
                var confirmation = CreateConfirmation(payment);

                Assert.True(
                    await service.CompleteAsync(confirmation));
                Assert.False(
                    await service.CompleteAsync(confirmation));
            }

            using var verificationScope =
                _factory.Services.CreateScope();

            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));

            var persistedJob = Assert.IsType<Job>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(
                        jobId,
                        CancellationToken.None));

            Assert.Equal(
                PaymentStatus.Succeeded,
                persistedPayment.Status);
            Assert.Equal(
                JobPaymentStatus.Paid,
                persistedJob.PaymentStatus);
            Assert.Equal(
                JobSettlementType.DirectPayment,
                persistedJob.SettlementType);
            Assert.Equal(
                payment.Id,
                persistedJob.SettlementReferenceId);

            var paymentAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                payment.Id);
            Assert.Single(
                paymentAudit.Items,
                item => item.Action == "payment.succeeded");

            var jobAudit = await FindAuditAsync(
                verificationScope.ServiceProvider,
                jobId);
            Assert.Single(
                jobAudit.Items,
                item => item.Action == "job.settled");

            var notifications =
                await verificationScope.ServiceProvider
                    .GetRequiredService<INotificationQueries>()
                    .ListRecentAsync();

            Assert.Single(
                notifications,
                item =>
                    item.RecipientUserId == customerUserId &&
                    item.Type == "payment.succeeded");

            Assert.Null(await FindDocumentAsync(payment.Id));
            Assert.Equal(
                counterBefore,
                await GetFinancialDocumentCounterTotalAsync());
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId);
        }
    }

    [Fact]
    public async Task CompleteAsync_CsobTopUpIssuesCanonicalDocumentOnce()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreateCsobTopUp(customerUserId);

        try
        {
            await AddCustomerAsync(
                customerUserId,
                "Settlement Customer",
                "settlement.customer@example.test");
            var orderNumber = await AddCsobPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();
                var confirmation = CreateConfirmation(payment);

                Assert.True(await service.CompleteAsync(confirmation));
                Assert.False(await service.CompleteAsync(confirmation));
            }

            using var verificationScope = _factory.Services.CreateScope();
            var persistedPayment = Assert.IsType<Payment>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));
            var account = Assert.IsType<CreditAccount>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        customerUserId,
                        CancellationToken.None));
            var movement = Assert.Single(account.Movements);
            var document = Assert.IsType<FinancialDocument>(
                await FindDocumentAsync(payment.Id));

            Assert.Equal(PaymentStatus.Succeeded, persistedPayment.Status);
            Assert.Equal(payment.Amount, account.Balance);
            Assert.Equal(payment.Id, movement.OperationId);
            Assert.Equal(
                FinancialDocumentType.CardWalletTopUp,
                document.DocumentType);
            Assert.Equal(
                FinancialDocumentSourceType.Payment,
                document.SourceType);
            Assert.Equal(payment.Id, document.SourceId);
            Assert.Equal(customerUserId, document.Customer.CustomerUserId);
            Assert.Equal("Settlement Customer", document.Customer.DisplayName);
            Assert.Equal(
                "settlement.customer@example.test",
                document.Customer.Email);
            Assert.Equal(payment.Amount.MinorUnits, document.AmountMinorUnits);
            Assert.Equal(Money.CurrencyCode, document.Currency);
            Assert.Equal(movement.RecordedAt, document.FinancialEventAt);
            Assert.Equal("Csob", document.Provider!.Provider);
            Assert.Equal(
                payment.ProviderReference,
                document.Provider.Reference);
            Assert.Equal(
                orderNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                document.Provider.OrderNumber);
            Assert.Null(document.Job);
            Assert.NotNull(document.Issuer);
            Assert.NotNull(document.Tax);
            Assert.Equal(1, await CountDocumentsAsync(payment.Id));
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_ConcurrentCsobTopUpIssuesOneDocument()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreateCsobTopUp(customerUserId);

        try
        {
            await AddCustomerAsync(customerUserId);
            await AddCsobPaymentAsync(payment);

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var confirmation = CreateConfirmation(payment);

            var results = await Task.WhenAll(
                firstScope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>()
                    .CompleteAsync(confirmation),
                secondScope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>()
                    .CompleteAsync(confirmation));

            Assert.Single(results, result => result);
            Assert.Single(results, result => !result);

            using var verificationScope = _factory.Services.CreateScope();
            var account = Assert.IsType<CreditAccount>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<ICreditAccountRepository>()
                    .FindByOwnerIdAsync(
                        customerUserId,
                        CancellationToken.None));

            Assert.Single(account.Movements);
            Assert.NotNull(await FindDocumentAsync(payment.Id));
            Assert.Equal(1, await CountDocumentsAsync(payment.Id));
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_CsobJobIssuesCanonicalDocumentOnce()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var serviceUnitId = Guid.NewGuid();
        var payment = CreateCsobJobPayment(customerUserId, jobId);

        try
        {
            await AddCustomerAsync(
                customerUserId,
                "Job Customer",
                "job.customer@example.test");
            await AddServiceUnitAsync(
                serviceUnitId,
                "Inactive Settlement Workshop");
            var job = await AddPublishedJobAsync(
                customerUserId,
                jobId,
                payment.Amount,
                serviceUnitId);
            await DeactivateServiceUnitAsync(serviceUnitId);
            var orderNumber = await AddCsobPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>();
                var confirmation = CreateConfirmation(payment);

                Assert.True(await service.CompleteAsync(confirmation));
                Assert.False(await service.CompleteAsync(confirmation));
            }

            using var verificationScope = _factory.Services.CreateScope();
            var persistedJob = Assert.IsType<Job>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(jobId, CancellationToken.None));
            var document = Assert.IsType<FinancialDocument>(
                await FindDocumentAsync(payment.Id));

            Assert.Equal(JobPaymentStatus.Paid, persistedJob.PaymentStatus);
            Assert.Equal(payment.Id, persistedJob.SettlementReferenceId);
            Assert.Equal(
                FinancialDocumentType.DirectJobCardPayment,
                document.DocumentType);
            Assert.Equal(payment.Id, document.SourceId);
            Assert.Equal("Job Customer", document.Customer.DisplayName);
            Assert.Equal("Csob", document.Provider!.Provider);
            Assert.Equal(
                payment.ProviderReference,
                document.Provider.Reference);
            Assert.Equal(
                orderNumber.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                document.Provider.OrderNumber);
            Assert.Equal(persistedJob.SettledAt, document.FinancialEventAt);
            Assert.Equal(job.Id, document.Job!.JobId);
            Assert.Equal(job.Number, document.Job.JobNumber);
            Assert.Equal(job.Title, document.Job.Title);
            Assert.Equal(job.Description, document.Job.Description);
            Assert.Equal(
                "Inactive Settlement Workshop",
                document.Job.ServiceUnitName);
            Assert.Equal(1, await CountDocumentsAsync(payment.Id));
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId,
                serviceUnitId);
        }
    }

    [Fact]
    public async Task CompleteAsync_ConcurrentCsobJobIssuesOneDocument()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var serviceUnitId = Guid.NewGuid();
        var payment = CreateCsobJobPayment(customerUserId, jobId);

        try
        {
            await AddCustomerAsync(customerUserId);
            await AddServiceUnitAsync(serviceUnitId);
            await AddPublishedJobAsync(
                customerUserId,
                jobId,
                payment.Amount,
                serviceUnitId);
            await AddCsobPaymentAsync(payment);

            using var firstScope = _factory.Services.CreateScope();
            using var secondScope = _factory.Services.CreateScope();
            var confirmation = CreateConfirmation(payment);

            var results = await Task.WhenAll(
                firstScope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>()
                    .CompleteAsync(confirmation),
                secondScope.ServiceProvider
                    .GetRequiredService<IPaymentSettlementService>()
                    .CompleteAsync(confirmation));

            Assert.Single(results, result => result);
            Assert.Single(results, result => !result);

            using var verificationScope = _factory.Services.CreateScope();
            var job = Assert.IsType<Job>(
                await verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(jobId, CancellationToken.None));

            Assert.Equal(JobPaymentStatus.Paid, job.PaymentStatus);
            Assert.Equal(payment.Id, job.SettlementReferenceId);
            Assert.NotNull(await FindDocumentAsync(payment.Id));
            Assert.Equal(1, await CountDocumentsAsync(payment.Id));
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId,
                serviceUnitId);
        }
    }

    [Fact]
    public async Task CompleteAsync_CsobMissingInitiationRollsBackTopUp()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreatePendingCsobTopUp(customerUserId);

        try
        {
            await AddCustomerAsync(customerUserId);
            await AddPaymentAsync(payment);
            var counterBefore = await GetFinancialDocumentCounterTotalAsync();

            using (var scope = _factory.Services.CreateScope())
            {
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => scope.ServiceProvider
                        .GetRequiredService<IPaymentSettlementService>()
                        .CompleteAsync(CreateConfirmation(payment)));
            }

            await AssertCsobTopUpWasRolledBackAsync(
                customerUserId,
                payment.Id);
            Assert.Equal(
                counterBefore,
                await GetFinancialDocumentCounterTotalAsync());
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_CsobMismatchedInitiationRollsBackTopUp()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreateCsobTopUp(customerUserId);

        try
        {
            await AddCustomerAsync(customerUserId);
            await AddCsobPaymentAsync(payment);
            await SetInitiationProviderAsync(
                payment.Id,
                PaymentProvider.Development);
            var counterBefore = await GetFinancialDocumentCounterTotalAsync();

            using (var scope = _factory.Services.CreateScope())
            {
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => scope.ServiceProvider
                        .GetRequiredService<IPaymentSettlementService>()
                        .CompleteAsync(CreateConfirmation(payment)));
            }

            await AssertCsobTopUpWasRolledBackAsync(
                customerUserId,
                payment.Id);
            Assert.Equal(
                counterBefore,
                await GetFinancialDocumentCounterTotalAsync());
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_CsobPreparedInitiationRollsBackTopUp()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreateCsobTopUp(customerUserId);

        try
        {
            await AddCustomerAsync(customerUserId);
            await AddCsobPaymentAsync(
                payment,
                initializeInitiation: false);
            var counterBefore = await GetFinancialDocumentCounterTotalAsync();

            using (var scope = _factory.Services.CreateScope())
            {
                var initiation = Assert.IsType<PaymentInitiation>(
                    await scope.ServiceProvider
                        .GetRequiredService<IPaymentInitiationRepository>()
                        .FindByPaymentIdAsync(payment.Id));

                Assert.Equal(
                    PaymentInitiationState.Prepared,
                    initiation.State);

                await Assert.ThrowsAsync<InvalidDataException>(
                    () => scope.ServiceProvider
                        .GetRequiredService<IPaymentSettlementService>()
                        .CompleteAsync(CreateConfirmation(payment)));
            }

            await AssertCsobTopUpWasRolledBackAsync(
                customerUserId,
                payment.Id);
            Assert.Equal(
                counterBefore,
                await GetFinancialDocumentCounterTotalAsync());
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId: null);
        }
    }

    [Fact]
    public async Task CompleteAsync_CsobFinalConcurrencyFailureRollsBackAndConsumesNumber()
    {
        const int businessYear = 2102;
        var issuedAt = new DateTimeOffset(
            businessYear,
            6,
            1,
            12,
            0,
            0,
            TimeSpan.Zero);
        var failedCustomerId = Guid.NewGuid();
        var failedPayment = CreateCsobTopUp(failedCustomerId);
        var successfulCustomerId = Guid.NewGuid();
        var successfulPayment = CreateCsobTopUp(successfulCustomerId);

        try
        {
            await DeleteFinancialDocumentCounterAsync(businessYear);
            await AddCustomerAsync(failedCustomerId);
            await AddCsobPaymentAsync(failedPayment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = CreateSettlementService(
                    scope.ServiceProvider,
                    new ThrowingSavePaymentRepository(
                        scope.ServiceProvider
                            .GetRequiredService<IPaymentRepository>(),
                        throwConcurrency: true),
                    issuedAt);

                await Assert.ThrowsAsync<PaymentConcurrencyException>(
                    () => service.CompleteAsync(
                        CreateConfirmation(failedPayment)));
            }

            await AssertCsobTopUpWasRolledBackAsync(
                failedCustomerId,
                failedPayment.Id);

            await AddCustomerAsync(successfulCustomerId);
            await AddCsobPaymentAsync(successfulPayment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = CreateSettlementService(
                    scope.ServiceProvider,
                    scope.ServiceProvider
                        .GetRequiredService<IPaymentRepository>(),
                    issuedAt);

                Assert.True(await service.CompleteAsync(
                    CreateConfirmation(successfulPayment)));
            }

            var document = Assert.IsType<FinancialDocument>(
                await FindDocumentAsync(successfulPayment.Id));
            Assert.Equal(
                $"FUA-{businessYear}-000002",
                document.DocumentNumber);
        }
        finally
        {
            await DeleteScenarioAsync(
                failedCustomerId,
                failedPayment.Id,
                jobId: null);
            await DeleteScenarioAsync(
                successfulCustomerId,
                successfulPayment.Id,
                jobId: null);
            await DeleteFinancialDocumentCounterAsync(businessYear);
        }
    }

    [Fact]
    public async Task CompleteAsync_CsobJobFinalSaveFailureRollsBackEffectAndDocument()
    {
        const int businessYear = 2103;
        var issuedAt = new DateTimeOffset(
            businessYear,
            6,
            1,
            12,
            0,
            0,
            TimeSpan.Zero);
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var serviceUnitId = Guid.NewGuid();
        var payment = CreateCsobJobPayment(customerUserId, jobId);

        try
        {
            await DeleteFinancialDocumentCounterAsync(businessYear);
            await AddCustomerAsync(customerUserId);
            await AddServiceUnitAsync(serviceUnitId);
            await AddPublishedJobAsync(
                customerUserId,
                jobId,
                payment.Amount,
                serviceUnitId);
            await AddCsobPaymentAsync(payment);

            using (var scope = _factory.Services.CreateScope())
            {
                var service = CreateFailingSettlementService(
                    scope.ServiceProvider,
                    issuedAt);

                await Assert.ThrowsAsync<TestPaymentSaveException>(
                    () => service.CompleteAsync(
                        CreateConfirmation(payment)));
            }

            using var verificationScope = _factory.Services.CreateScope();
            var services = verificationScope.ServiceProvider;
            var persistedPayment = Assert.IsType<Payment>(
                await services
                    .GetRequiredService<IPaymentRepository>()
                    .FindByIdAsync(payment.Id));
            var persistedJob = Assert.IsType<Job>(
                await services
                    .GetRequiredService<IJobRepository>()
                    .FindByIdAsync(jobId, CancellationToken.None));

            Assert.Equal(PaymentStatus.Pending, persistedPayment.Status);
            Assert.Equal(JobPaymentStatus.Unpaid, persistedJob.PaymentStatus);
            Assert.Null(persistedJob.SettlementType);
            Assert.Null(persistedJob.SettlementReferenceId);
            Assert.Null(persistedJob.SettledAt);
            Assert.Null(await FindDocumentAsync(payment.Id));
            Assert.Equal(
                1,
                await ReadFinancialDocumentCounterAsync(businessYear));

            var paymentAudit = await FindAuditAsync(services, payment.Id);
            Assert.Empty(paymentAudit.Items);
            var jobAudit = await FindAuditAsync(services, jobId);
            Assert.Empty(jobAudit.Items);

            var notifications = await services
                .GetRequiredService<INotificationQueries>()
                .ListRecentAsync();
            Assert.DoesNotContain(
                notifications,
                item => item.RecipientUserId == customerUserId);
        }
        finally
        {
            await DeleteScenarioAsync(
                customerUserId,
                payment.Id,
                jobId,
                serviceUnitId);
            await DeleteFinancialDocumentCounterAsync(businessYear);
        }
    }

    private static PaymentSettlementService
        CreateFailingSettlementService(
            IServiceProvider services,
            DateTimeOffset currentTime)
    {
        return CreateSettlementService(
            services,
            new ThrowingSavePaymentRepository(
                services.GetRequiredService<IPaymentRepository>()),
            currentTime);
    }

    private static PaymentSettlementService CreateSettlementService(
        IServiceProvider services,
        IPaymentRepository paymentRepository,
        DateTimeOffset currentTime)
    {
        return new PaymentSettlementService(
            paymentRepository,
            services.GetRequiredService<IPaymentInitiationRepository>(),
            services.GetRequiredService<CreditService>(),
            services.GetRequiredService<JobSettlementService>(),
            services.GetRequiredService<IAccessUserQueries>(),
            services.GetRequiredService<IServiceUnitRepository>(),
            services.GetRequiredService<IFinancialDocumentRepository>(),
            services.GetRequiredService<
                IFinancialDocumentNumberAllocator>(),
            services.GetRequiredService<
                IFinancialDocumentIssuanceProfile>(),
            services.GetRequiredService<IApplicationTransaction>(),
            new FixedTimeProvider(currentTime),
            services.GetRequiredService<IAuditTrail>(),
            services.GetRequiredService<INotificationOutbox>());
    }

    private async Task AddCustomerAsync(
        Guid customerUserId,
        string displayName = "Settlement Customer",
        string? email = "settlement@example.test")
    {
        using var scope = _factory.Services.CreateScope();
        var user = new AccessUser(
            customerUserId,
            displayName,
            email,
            TestTime);

        await scope.ServiceProvider
            .GetRequiredService<IAccessUserRepository>()
            .AddAsync(
                user,
                new ExternalIdentityKey(
                    "payment-settlement-tests",
                    "fuapay",
                    customerUserId.ToString("N")),
                CancellationToken.None);
    }

    private async Task<long> AddCsobPaymentAsync(
        Payment payment,
        bool initializeInitiation = true)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var orderNumber = await services
            .GetRequiredService<IPaymentOrderNumberAllocator>()
            .AllocateAsync();
        var initiation = new PaymentInitiation(
            payment.Id,
            payment.Provider,
            orderNumber,
            Guid.NewGuid(),
            TestTime);

        if (initializeInitiation)
        {
            initiation.Begin(TestTime.AddMinutes(1));
            initiation.Complete(TestTime.AddMinutes(2));
        }

        var repository = services.GetRequiredService<IPaymentRepository>();
        await repository.AddPreparedAsync(payment, initiation);
        payment.MarkPending(
            $"CSOB-SETTLEMENT-{Guid.NewGuid():N}",
            TestTime.AddMinutes(3));
        await repository.SaveAsync(payment);

        return orderNumber;
    }

    private async Task AddServiceUnitAsync(
        Guid serviceUnitId,
        string displayName = "Settlement Workshop")
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IServiceUnitRepository>();
        var serviceUnit = new ServiceUnit(
            serviceUnitId,
            $"S{serviceUnitId:N}"[..8].ToUpperInvariant(),
            displayName,
            ServiceType.ThreeDPrint,
            TestTime,
            ServiceUnitChangeActor.ForProcess(
                "payment-settlement-tests"));

        await repository.AddAsync(serviceUnit, CancellationToken.None);
    }

    private async Task DeactivateServiceUnitAsync(Guid serviceUnitId)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<IServiceUnitRepository>();
        var serviceUnit = Assert.IsType<ServiceUnit>(
            await repository.FindByIdAsync(
                serviceUnitId,
                CancellationToken.None));

        serviceUnit.Deactivate(
            TestTime.AddMinutes(10),
            ServiceUnitChangeActor.ForProcess(
                "payment-settlement-tests"));
        await repository.SaveAsync(
            serviceUnit,
            CancellationToken.None);
    }

    private async Task AddPaymentAsync(Payment payment)
    {
        using var scope = _factory.Services.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<IPaymentRepository>()
            .AddAsync(payment);
    }

    private async Task<Job> AddPublishedJobAsync(
        Guid customerUserId,
        Guid jobId,
        Money price,
        Guid? serviceUnitId = null)
    {
        using var scope = _factory.Services.CreateScope();

        var job = new Job(
            jobId,
            TestJobData.NextJobNumber(),
            serviceUnitId ?? Guid.NewGuid(),
            customerUserId,
            customerUserId,
            ServiceType.ThreeDPrint,
            "Provider settlement test",
            "Zakázka pro databázový test vypořádání platby.",
            price,
            TestTime);

        job.Publish(TestTime.AddMinutes(5));

        await scope.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .AddAsync(
                job,
                CancellationToken.None);

        return job;
    }

    private static Payment CreatePendingTopUp(
        Guid customerUserId,
        string providerReference)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(25_000),
            PaymentProvider.Development,
            TestTime,
            Guid.NewGuid());

        payment.MarkPending(
            providerReference,
            TestTime.AddMinutes(1));

        return payment;
    }

    private static Payment CreateCsobTopUp(Guid customerUserId)
    {
        return new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(25_000),
            PaymentProvider.Csob,
            TestTime,
            Guid.NewGuid());
    }

    private static Payment CreatePendingCsobTopUp(Guid customerUserId)
    {
        var payment = CreateCsobTopUp(customerUserId);
        payment.MarkPending(
            $"CSOB-MISSING-INITIATION-{Guid.NewGuid():N}",
            TestTime.AddMinutes(3));
        return payment;
    }

    private static Payment CreatePendingJobPayment(
        Guid customerUserId,
        Guid jobId,
        string providerReference)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.Job,
            jobId,
            new Money(42_000),
            PaymentProvider.Development,
            TestTime);

        payment.MarkPending(
            providerReference,
            TestTime.AddMinutes(1));

        return payment;
    }

    private static Payment CreateCsobJobPayment(
        Guid customerUserId,
        Guid jobId)
    {
        return new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.Job,
            jobId,
            new Money(42_000),
            PaymentProvider.Csob,
            TestTime);
    }

    private static VerifiedPaymentConfirmation CreateConfirmation(
        Payment payment)
    {
        return new VerifiedPaymentConfirmation(
            payment.Provider,
            payment.ProviderReference!,
            payment.Amount);
    }

    private static Task<AuditPage> FindAuditAsync(
        IServiceProvider services,
        Guid entityId)
    {
        return services.GetRequiredService<IAuditQueries>()
            .ListAsync(
                new AuditListFilter(Search: entityId.ToString()),
                new AuditPageRequest(limit: 100));
    }

    private async Task<FinancialDocument?> FindDocumentAsync(Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IFinancialDocumentRepository>()
            .FindBySourceAsync(
                FinancialDocumentSourceType.Payment,
                paymentId);
    }

    private async Task<int> CountDocumentsAsync(Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM financial_documents.documents
                WHERE source_type = {(int)FinancialDocumentSourceType.Payment}
                  AND source_id = {paymentId}
                """)
            .SingleAsync();
    }

    private async Task<long> GetFinancialDocumentCounterTotalAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT COALESCE(sum(last_value), 0)::bigint AS "Value"
                FROM financial_documents.number_counters
                """)
            .SingleAsync();
    }

    private async Task SetInitiationProviderAsync(
        Guid paymentId,
        PaymentProvider provider)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE payments.payment_initiations
            SET provider = {(int)provider}
            WHERE payment_id = {paymentId}
            """);
    }

    private async Task AssertCsobTopUpWasRolledBackAsync(
        Guid customerUserId,
        Guid paymentId)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var payment = Assert.IsType<Payment>(
            await services
                .GetRequiredService<IPaymentRepository>()
                .FindByIdAsync(paymentId));
        var account = await services
            .GetRequiredService<ICreditAccountRepository>()
            .FindByOwnerIdAsync(
                customerUserId,
                CancellationToken.None);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(account);
        Assert.Null(await FindDocumentAsync(paymentId));
    }

    private async Task DeleteFinancialDocumentCounterAsync(int businessYear)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM financial_documents.number_counters
            WHERE business_year = {businessYear}
            """);
    }

    private async Task<int> ReadFinancialDocumentCounterAsync(int businessYear)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database
            .SqlQuery<int>(
                $"""
                SELECT last_value AS "Value"
                FROM financial_documents.number_counters
                WHERE business_year = {businessYear}
                """)
            .SingleAsync();
    }

    private async Task DeleteScenarioAsync(
        Guid customerUserId,
        Guid paymentId,
        Guid? jobId,
        Guid? serviceUnitId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM financial_documents.documents
            WHERE source_type = {(int)FinancialDocumentSourceType.Payment}
              AND source_id = {paymentId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM notifications.outbox
            WHERE recipient_user_id = {customerUserId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE entity_id IN
            (
                {paymentId.ToString()},
                {(jobId ?? Guid.Empty).ToString()}
            )
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE id = {paymentId}
            """);

        if (jobId.HasValue)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM jobs.jobs
                WHERE id = {jobId.Value}
                """);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.movements
            WHERE account_id IN
            (
                SELECT id
                FROM credits.accounts
                WHERE owner_id = {customerUserId}
            )
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.accounts
            WHERE owner_id = {customerUserId}
            """);

        if (serviceUnitId.HasValue)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM service_units.units
                WHERE id = {serviceUnitId.Value}
                """);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.external_identities
            WHERE user_id = {customerUserId}
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.users
            WHERE id = {customerUserId}
            """);

        await transaction.CommitAsync();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }

    private sealed class ThrowingSavePaymentRepository :
        IPaymentRepository
    {
        private readonly IPaymentRepository _inner;
        private readonly bool _throwConcurrency;

        internal ThrowingSavePaymentRepository(
            IPaymentRepository inner,
            bool throwConcurrency = false)
        {
            _inner = inner;
            _throwConcurrency = throwConcurrency;
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

        public Task AddAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            return _inner.AddAsync(
                payment,
                cancellationToken);
        }

        public Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default)
        {
            return _inner.AddPreparedAsync(
                payment,
                initiation,
                cancellationToken);
        }

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            if (_throwConcurrency)
            {
                throw new PaymentConcurrencyException(payment.Id);
            }

            throw new TestPaymentSaveException();
        }
    }

    private sealed class TestPaymentSaveException : Exception
    {
    }
}
