using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class PaymentPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 15, 30, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PaymentPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RepositoryAndQueries_RoundTripSucceededPayment()
    {
        var payment = CreateTopUpPayment(
            $"DEV-TOP-UP-{Guid.NewGuid():N}");

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                var repository = createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>();

                await repository.AddAsync(payment);
            }

            using (var updateScope = _factory.Services.CreateScope())
            {
                var repository = updateScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>();
                var loaded = Assert.IsType<Payment>(
                    await repository.FindByIdAsync(payment.Id));
                var byProviderReference = Assert.IsType<Payment>(
                    await repository.FindByProviderReferenceAsync(
                        payment.Provider,
                        $"  {payment.ProviderReference}  "));

                Assert.Equal(payment.Id, byProviderReference.Id);

                loaded.Complete(CreatedAt.AddMinutes(1));
                await repository.SaveAsync(loaded);
            }

            using var queryScope = _factory.Services.CreateScope();
            var queries = queryScope.ServiceProvider
                .GetRequiredService<IPaymentQueries>();

            var detail = Assert.IsType<PaymentDetail>(
                await queries.FindForCustomerAsync(
                    payment.CustomerUserId,
                    payment.Id));

            Assert.Equal(PaymentStatus.Succeeded, detail.Status);
            Assert.Equal(payment.Amount.MinorUnits, detail.AmountMinorUnits);
            Assert.Equal(payment.ProviderReference, detail.ProviderReference);
            Assert.Equal(CreatedAt.AddMinutes(1), detail.CompletedAt);
            Assert.Equal(2, detail.Version);
        }
        finally
        {
            await DeletePaymentsAsync(payment.Id);
        }
    }

    [Fact]
    public async Task Database_RejectsSecondOpenPaymentForSameJob()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var first = CreateJobPayment(customerUserId, jobId, "DEV-FIRST");
        var second = CreateJobPayment(customerUserId, jobId, "DEV-SECOND");

        try
        {
            using (var firstScope = _factory.Services.CreateScope())
            {
                await firstScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddAsync(first);
            }

            using var secondScope = _factory.Services.CreateScope();
            var repository = secondScope.ServiceProvider
                .GetRequiredService<IPaymentRepository>();

            await Assert.ThrowsAsync<PaymentConcurrencyException>(
                () => repository.AddAsync(second));
        }
        finally
        {
            await DeletePaymentsAsync(first.Id, second.Id);
        }
    }

    [Theory]
    [InlineData(PaymentStatus.Created)]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Succeeded)]
    public async Task Database_BlockingPolicyRejectsSecondJobPayment(
        PaymentStatus blockingStatus)
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var first = CreateJobPayment(
            customerUserId,
            jobId,
            $"DEV-BLOCKING-{blockingStatus}-{Guid.NewGuid():N}",
            blockingStatus);
        var second = CreateJobPayment(
            customerUserId,
            jobId,
            $"DEV-SECOND-{Guid.NewGuid():N}");

        try
        {
            using (var firstScope = _factory.Services.CreateScope())
            {
                await firstScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddAsync(first);
            }

            using var secondScope = _factory.Services.CreateScope();
            await Assert.ThrowsAsync<PaymentConcurrencyException>(
                () => secondScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddAsync(second));
        }
        finally
        {
            await DeletePaymentsAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task Database_RejectsDuplicateProviderReference()
    {
        var reference = $"DEV-DUPLICATE-{Guid.NewGuid():N}";
        var first = CreateTopUpPayment(reference);
        var second = CreateTopUpPayment(reference);

        try
        {
            using (var firstScope = _factory.Services.CreateScope())
            {
                await firstScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddAsync(first);
            }

            using var secondScope = _factory.Services.CreateScope();
            var repository = secondScope.ServiceProvider
                .GetRequiredService<IPaymentRepository>();

            await Assert.ThrowsAsync<PaymentConcurrencyException>(
                () => repository.AddAsync(second));
        }
        finally
        {
            await DeletePaymentsAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task FailedJobPayment_AllowsNewPaymentAttempt()
    {
        var customerUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var failed = CreateJobPayment(
            customerUserId,
            jobId,
            "DEV-FAILED");
        var retry = CreateJobPayment(
            customerUserId,
            jobId,
            "DEV-RETRY");

        try
        {
            using (var createScope = _factory.Services.CreateScope())
            {
                await createScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>()
                    .AddAsync(failed);
            }

            using (var failScope = _factory.Services.CreateScope())
            {
                var repository = failScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>();
                var loaded = Assert.IsType<Payment>(
                    await repository.FindByIdAsync(failed.Id));

                loaded.Fail(
                    "Testovací zamítnutí.",
                    CreatedAt.AddMinutes(1));

                await repository.SaveAsync(loaded);
            }

            using (var retryScope = _factory.Services.CreateScope())
            {
                var repository = retryScope.ServiceProvider
                    .GetRequiredService<IPaymentRepository>();

                Assert.Null(
                    await repository.FindBlockingForJobAsync(jobId));

                await repository.AddAsync(retry);
            }

            using var verifyScope = _factory.Services.CreateScope();
            var verificationRepository = verifyScope.ServiceProvider
                .GetRequiredService<IPaymentRepository>();

            var open = Assert.IsType<Payment>(
                await verificationRepository.FindBlockingForJobAsync(jobId));

            Assert.Equal(retry.Id, open.Id);
            Assert.Equal(PaymentStatus.Pending, open.Status);
        }
        finally
        {
            await DeletePaymentsAsync(failed.Id, retry.Id);
        }
    }

    private static Payment CreateTopUpPayment(string reference)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(50_000),
            PaymentProvider.Development,
            CreatedAt,
            Guid.NewGuid());
        payment.MarkPending(reference, CreatedAt);
        return payment;
    }

    private static Payment CreateJobPayment(
        Guid customerUserId,
        Guid jobId,
        string reference,
        PaymentStatus status = PaymentStatus.Pending)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.Job,
            jobId,
            new Money(42_000),
            PaymentProvider.Development,
            CreatedAt);
        if (status != PaymentStatus.Created)
        {
            payment.MarkPending(reference, CreatedAt);
        }

        if (status == PaymentStatus.Succeeded)
        {
            payment.Complete(CreatedAt.AddMinutes(1));
        }

        return payment;
    }

    private async Task DeletePaymentsAsync(params Guid[] paymentIds)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM payments.payments
            WHERE id = ANY ({paymentIds})
            """);
    }
}
