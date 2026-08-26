using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Tests.Modules.Jobs.Application;

public sealed class CreditJobPaymentServiceTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(
            2026,
            7,
            28,
            14,
            30,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task PayAsync_PublishedOwnedJob_DebitsCreditAndMarksJobPaid()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedJob(customerUserId);
        var account = CreateFundedAccount(customerUserId);
        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);

        var transaction = new FakeApplicationTransaction();

        var service = CreateService(
            jobRepository,
            creditRepository,
            transaction);

        var wasApplied = await service.PayAsync(
            customerUserId,
            job.Id);

        Assert.True(wasApplied);
        Assert.Equal(new Money(7_500), account.Balance);
        Assert.Equal(JobPaymentStatus.Paid, job.PaymentStatus);
        Assert.Equal(JobSettlementType.Credit, job.SettlementType);
        Assert.Equal(job.Id, job.SettlementReferenceId);
        Assert.Equal(CurrentTime, job.SettledAt);
        Assert.Equal(1, creditRepository.SaveCalls);
        Assert.Equal(1, jobRepository.SaveCalls);
        Assert.Equal(1, transaction.ExecuteCalls);

        var debitMovement = Assert.Single(
            account.Movements,
            movement =>
                movement.OperationId == job.Id);

        Assert.Equal(
            CreditMovementType.Debit,
            debitMovement.Type);

        Assert.Equal(
            job.Price,
            debitMovement.Amount);
    }

    [Fact]
    public async Task PayAsync_SamePaymentRepeated_DoesNotDebitAgain()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedJob(customerUserId);
        var account = CreateFundedAccount(customerUserId);

        _ = account.Debit(
            job.Id,
            job.Price,
            CurrentTime.AddMinutes(-1),
            "První úhrada zakázky");

        _ = job.ConfirmSettlement(
            JobSettlementType.Credit,
            job.Id,
            CurrentTime.AddMinutes(-1));

        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);

        var service = CreateService(
            jobRepository,
            creditRepository,
            new FakeApplicationTransaction());

        var wasApplied = await service.PayAsync(
            customerUserId,
            job.Id);

        Assert.False(wasApplied);
        Assert.Equal(new Money(7_500), account.Balance);
        Assert.Equal(0, creditRepository.FindCalls);
        Assert.Equal(0, creditRepository.SaveCalls);
        Assert.Equal(0, jobRepository.SaveCalls);
        Assert.Single(
            account.Movements,
            movement =>
                movement.OperationId == job.Id);
    }

    [Fact]
    public async Task PayAsync_AlreadyPaidByDirectPayment_ThrowsWithoutDebit()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedJob(customerUserId);
        var account = CreateFundedAccount(customerUserId);

        _ = job.ConfirmSettlement(
            JobSettlementType.DirectPayment,
            Guid.NewGuid(),
            CurrentTime.AddMinutes(-1));

        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);

        var service = CreateService(
            jobRepository,
            creditRepository,
            new FakeApplicationTransaction());

        await Assert.ThrowsAsync<
            JobSettlementConflictException>(
                () => service.PayAsync(
                    customerUserId,
                    job.Id));

        Assert.Equal(new Money(20_000), account.Balance);
        Assert.Equal(0, creditRepository.FindCalls);
        Assert.Equal(0, creditRepository.SaveCalls);
        Assert.Equal(0, jobRepository.SaveCalls);
    }

    [Fact]
    public async Task PayAsync_ForeignCustomer_ThrowsWithoutChanges()
    {
        var job = CreatePublishedJob(Guid.NewGuid());
        var account = CreateFundedAccount(Guid.NewGuid());
        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);

        var service = CreateService(
            jobRepository,
            creditRepository,
            new FakeApplicationTransaction());

        var customerUserId = Guid.NewGuid();

        var exception =
            await Assert.ThrowsAsync<
                JobPaymentAccessDeniedException>(
                () => service.PayAsync(
                    customerUserId,
                    job.Id));

        Assert.Equal(job.Id, exception.JobId);
        Assert.Equal(
            customerUserId,
            exception.CustomerUserId);

        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        Assert.Equal(0, creditRepository.FindCalls);
        Assert.Equal(0, creditRepository.SaveCalls);
        Assert.Equal(0, jobRepository.SaveCalls);
    }

    [Fact]
    public async Task PayAsync_InsufficientCredit_DoesNotMarkJobPaid()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedJob(customerUserId);
        var account = new CreditAccount(
            Guid.NewGuid(),
            customerUserId);

        _ = account.Credit(
            Guid.NewGuid(),
            new Money(1_000),
            CurrentTime.AddHours(-2),
            "Malý počáteční kredit");

        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);

        var service = CreateService(
            jobRepository,
            creditRepository,
            new FakeApplicationTransaction());

        await Assert.ThrowsAsync<InsufficientCreditException>(
            () => service.PayAsync(
                customerUserId,
                job.Id));

        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        Assert.Null(job.SettlementType);
        Assert.Null(job.SettlementReferenceId);
        Assert.Null(job.SettledAt);
        Assert.Equal(new Money(1_000), account.Balance);
        Assert.Equal(0, creditRepository.SaveCalls);
        Assert.Equal(0, jobRepository.SaveCalls);
    }

    [Fact]
    public async Task PayAsync_DraftJob_DoesNotDebitCredit()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreateDraftJob(customerUserId);
        var account = CreateFundedAccount(customerUserId);
        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);

        var service = CreateService(
            jobRepository,
            creditRepository,
            new FakeApplicationTransaction());

        await Assert.ThrowsAsync<
            JobSettlementNotAllowedException>(
                () => service.PayAsync(
                    customerUserId,
                    job.Id));

        Assert.Equal(new Money(20_000), account.Balance);
        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        Assert.Equal(0, creditRepository.FindCalls);
        Assert.Equal(0, creditRepository.SaveCalls);
        Assert.Equal(0, jobRepository.SaveCalls);
    }

    [Fact]
    public async Task PayAsync_BlockingDirectPayment_DoesNotDebitCredit()
    {
        var customerUserId = Guid.NewGuid();
        var job = CreatePublishedJob(customerUserId);
        var account = CreateFundedAccount(customerUserId);
        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);
        var coordination = new FakeJobPaymentCoordination
        {
            HasBlockingDirectPayment = true
        };

        var service = CreateService(
            jobRepository,
            creditRepository,
            new FakeApplicationTransaction(),
            coordination);

        var exception = await Assert.ThrowsAsync<
            JobPaymentInProgressException>(
                () => service.PayAsync(
                    customerUserId,
                    job.Id));

        Assert.Equal(job.Id, exception.JobId);
        Assert.Equal(new Money(20_000), account.Balance);
        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        Assert.Equal(0, creditRepository.FindCalls);
        Assert.Equal(0, creditRepository.SaveCalls);
        Assert.Equal(0, jobRepository.SaveCalls);
    }

    private static CreditJobPaymentService CreateService(
        IJobRepository jobRepository,
        ICreditAccountRepository creditRepository,
        IApplicationTransaction transaction,
        IJobPaymentCoordination? coordination = null)
    {
        var creditService = new CreditService(
            creditRepository,
            new NoBlockingPrintReservationRepository(),
            transaction,
            new FixedTimeProvider(CurrentTime));

        return new CreditJobPaymentService(
            jobRepository,
            coordination ?? new FakeJobPaymentCoordination(),
            creditService,
            transaction,
            NullAuditTrail.Instance,
            NullNotificationOutbox.Instance);
    }

    private static Job CreateDraftJob(
        Guid customerUserId)
    {
        return new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            customerUserId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Model",
            "Tisk modelu",
            new Money(12_500),
            CurrentTime.AddHours(-2));
    }

    private static Job CreatePublishedJob(
        Guid customerUserId)
    {
        var job = CreateDraftJob(customerUserId);

        job.Publish(
            CurrentTime.AddHours(-1));

        return job;
    }

    private static CreditAccount CreateFundedAccount(
        Guid customerUserId)
    {
        var account = new CreditAccount(
            Guid.NewGuid(),
            customerUserId);

        _ = account.Credit(
            Guid.NewGuid(),
            new Money(20_000),
            CurrentTime.AddHours(-2),
            "Počáteční kredit");

        return account;
    }

    private sealed class FixedTimeProvider :
        TimeProvider
    {
        private readonly DateTimeOffset _currentTime;

        public FixedTimeProvider(
            DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _currentTime;
        }
    }

    private sealed class FakeApplicationTransaction :
        IApplicationTransaction
    {
        public int ExecuteCalls { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            cancellationToken.ThrowIfCancellationRequested();

            if (_isActive)
            {
                return await operation(cancellationToken);
            }

            ExecuteCalls++;
            _isActive = true;

            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                _isActive = false;
            }
        }

        private bool _isActive;
    }

    private sealed class FakeJobPaymentCoordination :
        IJobPaymentCoordination
    {
        public bool HasBlockingDirectPayment { get; set; }

        public Task<bool> LockJobAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> HasBlockingDirectPaymentAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(HasBlockingDirectPayment);
    }

    private sealed class FakeJobRepository :
        IJobRepository
    {
        public FakeJobRepository(Job job)
        {
            ArgumentNullException.ThrowIfNull(job);

            Job = job;
        }

        public Job Job { get; }

        public int SaveCalls { get; private set; }

        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Job? result =
                Job.Id == jobId
                    ? Job
                    : null;

            return Task.FromResult(result);
        }

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(
            Job job,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(Job, job);

            SaveCalls++;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeCreditAccountRepository :
        ICreditAccountRepository
    {
        public FakeCreditAccountRepository(
            CreditAccount account)
        {
            ArgumentNullException.ThrowIfNull(account);

            Account = account;
        }

        public CreditAccount Account { get; }

        public int FindCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCalls++;

            CreditAccount? result =
                Account.OwnerId == ownerId
                    ? Account
                    : null;

            return Task.FromResult(result);
        }

        public Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            FindByOwnerIdAsync(ownerId, cancellationToken);

        public Task LockOwnerForAccountCreationAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AddAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(Account, account);

            SaveCalls++;

            return Task.CompletedTask;
        }
    }

    private sealed class NoBlockingPrintReservationRepository :
        IPrintReservationRepository
    {
        public Task<PrintReservationResult?> FindByReserveCommandAsync(
            Guid printSourceId,
            Guid reserveCommandId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PrintReservationResult?> FindByPrintJobAsync(
            Guid printSourceId,
            string jobUuid,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Money> GetBlockingAmountAsync(
            Guid creditAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Money.Zero);

        public Task AddAsync(
            PrintReservation reservation,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
