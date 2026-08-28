using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Application;

public sealed class CreditJobSettlementReturnServiceTests
{
    private static readonly DateTimeOffset ReturnTime =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Command_ExposesOnlyRequestJobAdministratorAndReason()
    {
        var properties = typeof(CreditJobSettlementReturnCommand)
            .GetProperties()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            new[]
            {
                nameof(CreditJobSettlementReturnCommand.AdministratorUserId),
                nameof(CreditJobSettlementReturnCommand.JobId),
                nameof(CreditJobSettlementReturnCommand.Reason),
                nameof(CreditJobSettlementReturnCommand.RequestId)
            },
            properties);
    }

    [Fact]
    public async Task ReturnAsync_CompletedCreditJob_AppendsFullCreditAndAudits()
    {
        var fixture = CreateFixture();
        var originalDebit = Assert.Single(
            fixture.Account.Movements,
            movement => movement.OperationId == fixture.Job.Id);
        var originalState = CaptureJobState(fixture.Job);

        var result = await fixture.Service.ReturnAsync(
            fixture.Command);

        Assert.True(result.Created);
        Assert.Equal(
            SettlementReturnState.Completed,
            result.SettlementReturn.State);
        Assert.Equal(
            SettlementReturnKind.CreditJob,
            result.SettlementReturn.Kind);
        Assert.Equal(fixture.Job.Id, result.SettlementReturn.JobId);
        Assert.Null(result.SettlementReturn.OriginalPaymentId);
        Assert.Equal(
            fixture.Job.CustomerUserId,
            result.SettlementReturn.CustomerUserId);
        Assert.Equal(
            fixture.Command.AdministratorUserId,
            result.SettlementReturn.AdministratorUserId);
        Assert.Equal(fixture.Job.Price, result.SettlementReturn.Amount);
        Assert.Equal("CZK", result.SettlementReturn.Currency);
        Assert.Equal("Administrative reason", result.SettlementReturn.Reason);

        Assert.Equal(new Money(20_000), fixture.Account.Balance);
        Assert.Same(
            originalDebit,
            Assert.Single(
                fixture.Account.Movements,
                movement => movement.OperationId == fixture.Job.Id));

        var compensation = Assert.Single(
            fixture.Account.Movements,
            movement =>
                movement.OperationId == result.SettlementReturn.Id);

        Assert.Equal(CreditMovementType.Credit, compensation.Type);
        Assert.Equal(fixture.Job.Price, compensation.Amount);
        Assert.Equal(1, fixture.CreditRepository.SaveCalls);
        Assert.Equal(1, fixture.ReturnRepository.AddCalls);
        Assert.Equal(1, fixture.ReturnRepository.SaveCalls);
        Assert.Equal(1, fixture.Transaction.ExecuteCalls);
        Assert.Equal(originalState, CaptureJobState(fixture.Job));

        var audit = Assert.Single(fixture.AuditTrail.Entries);
        Assert.Equal(
            fixture.Command.AdministratorUserId,
            audit.ActorUserId);
        Assert.Equal(
            "settlement-return.credit-job.completed",
            audit.Action);
        Assert.Equal(
            result.SettlementReturn.Id.ToString(),
            audit.EntityId);
        Assert.Contains(fixture.Job.Id.ToString(), audit.Description);
        Assert.Contains(
            fixture.Job.CustomerUserId.ToString(),
            audit.Description);
        Assert.Contains(
            fixture.Job.Price.MinorUnits.ToString(),
            audit.Description);
        Assert.Contains(fixture.Command.Reason, audit.Description);
    }

    [Fact]
    public async Task ReturnAsync_SameRequestReplayed_ReturnsExistingEffectOnce()
    {
        var fixture = CreateFixture();

        var first = await fixture.Service.ReturnAsync(fixture.Command);
        var replay = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Same(first.SettlementReturn, replay.SettlementReturn);
        Assert.Single(
            fixture.Account.Movements,
            movement =>
                movement.OperationId == first.SettlementReturn.Id);
        Assert.Equal(1, fixture.CreditRepository.SaveCalls);
        Assert.Equal(1, fixture.ReturnRepository.AddCalls);
        Assert.Equal(1, fixture.ReturnRepository.SaveCalls);
        Assert.Single(fixture.AuditTrail.Entries);

        await Assert.ThrowsAsync<
            SettlementReturnRequestConflictException>(
                () => fixture.Service.ReturnAsync(
                    fixture.Command with
                    {
                        Reason = "Conflicting reason"
                    }));

        Assert.Equal(1, fixture.CreditRepository.SaveCalls);
        Assert.Single(fixture.AuditTrail.Entries);
    }

    [Theory]
    [InlineData(SettlementReturnState.Requested)]
    [InlineData(SettlementReturnState.InProgress)]
    [InlineData(SettlementReturnState.RequiresAttention)]
    [InlineData(SettlementReturnState.Rejected)]
    [InlineData(SettlementReturnState.Completed)]
    public async Task ReturnAsync_PreExistingReturnWithoutMatchingCredit_FailsClosed(
        SettlementReturnState state)
    {
        var fixture = CreateFixture();
        var existing = CreateExistingReturn(fixture, state);

        await fixture.ReturnRepository.AddAsync(
            existing,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<
            CreditJobSettlementReturnEffectInconsistentException>(
                () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(existing.Id, exception.SettlementReturnId);
        Assert.Equal(new Money(7_500), fixture.Account.Balance);
        Assert.DoesNotContain(
            fixture.Account.Movements,
            movement => movement.OperationId == existing.Id);
        Assert.Equal(0, fixture.CreditRepository.SaveCalls);
        Assert.Empty(fixture.AuditTrail.Entries);
    }

    private static TestFixture CreateFixture()
    {
        var customerUserId = Guid.NewGuid();
        var administratorUserId = Guid.NewGuid();
        var job = CreateCompletedCreditPaidJob(customerUserId);
        var account = CreatePaidAccount(job);
        var jobRepository = new FakeJobRepository(job);
        var creditRepository =
            new FakeCreditAccountRepository(account);
        var transaction = new FakeApplicationTransaction();
        var creditQueries = new FakeCreditQueries(account);
        var returnRepository = new FakeSettlementReturnRepository();
        var auditTrail = new RecordingAuditTrail();
        var timeProvider = new FixedTimeProvider(ReturnTime);
        var creditService = new CreditService(
            creditRepository,
            new NoBlockingPrintReservationRepository(),
            transaction,
            timeProvider);
        var registrationService =
            new SettlementReturnRegistrationService(
                returnRepository);
        var service = new CreditJobSettlementReturnService(
            jobRepository,
            new FakeJobPaymentCoordination(),
            creditQueries,
            creditService,
            registrationService,
            returnRepository,
            transaction,
            auditTrail,
            timeProvider);
        var command = new CreditJobSettlementReturnCommand(
            Guid.NewGuid(),
            job.Id,
            administratorUserId,
            "Administrative reason");

        return new TestFixture(
            service,
            command,
            job,
            account,
            creditRepository,
            returnRepository,
            transaction,
            auditTrail);
    }

    private static Job CreateCompletedCreditPaidJob(Guid customerUserId)
    {
        var job = new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            customerUserId,
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Model",
            "Model paid from FUA Pay credit",
            new Money(12_500),
            ReturnTime.AddHours(-5));

        job.Publish(ReturnTime.AddHours(-4));
        job.ConfirmSettlement(
            JobSettlementType.Credit,
            job.Id,
            ReturnTime.AddHours(-3));
        job.StartProduction(ReturnTime.AddHours(-2));
        job.MarkReadyForPickup(ReturnTime.AddHours(-1));
        job.Complete(ReturnTime.AddMinutes(-30));

        return job;
    }

    private static CreditAccount CreatePaidAccount(Job job)
    {
        var account = new CreditAccount(
            Guid.NewGuid(),
            job.CustomerUserId);

        account.Credit(
            Guid.NewGuid(),
            new Money(20_000),
            ReturnTime.AddHours(-4),
            "Initial credit");
        account.Debit(
            job.Id,
            job.Price,
            job.SettledAt!.Value,
            "Original Job payment");

        return account;
    }

    private static SettlementReturn CreateExistingReturn(
        TestFixture fixture,
        SettlementReturnState state)
    {
        var settlementReturn = new SettlementReturn(
            Guid.NewGuid(),
            fixture.Command.RequestId,
            SettlementReturnKind.CreditJob,
            originalPaymentId: null,
            fixture.Job.Id,
            fixture.Job.CustomerUserId,
            fixture.Command.AdministratorUserId,
            fixture.Job.Price,
            fixture.Command.Reason,
            ReturnTime);

        if (state != SettlementReturnState.Requested)
        {
            settlementReturn.Begin(ReturnTime);
        }

        switch (state)
        {
            case SettlementReturnState.Requested:
            case SettlementReturnState.InProgress:
                break;
            case SettlementReturnState.RequiresAttention:
                settlementReturn.RequireAttention(ReturnTime);
                break;
            case SettlementReturnState.Rejected:
                settlementReturn.Reject(ReturnTime);
                break;
            case SettlementReturnState.Completed:
                settlementReturn.Complete(ReturnTime);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        return settlementReturn;
    }

    private static JobState CaptureJobState(Job job)
    {
        return new JobState(
            job.PaymentStatus,
            job.SettlementType,
            job.SettlementReferenceId,
            job.SettledAt,
            job.ProductionStatus,
            job.ProductionStartedAt,
            job.ReadyForPickupAt,
            job.CompletedAt,
            job.CancelledAt);
    }

    private sealed record TestFixture(
        CreditJobSettlementReturnService Service,
        CreditJobSettlementReturnCommand Command,
        Job Job,
        CreditAccount Account,
        FakeCreditAccountRepository CreditRepository,
        FakeSettlementReturnRepository ReturnRepository,
        FakeApplicationTransaction Transaction,
        RecordingAuditTrail AuditTrail);

    private sealed record JobState(
        JobPaymentStatus PaymentStatus,
        JobSettlementType? SettlementType,
        Guid? SettlementReferenceId,
        DateTimeOffset? SettledAt,
        JobProductionStatus ProductionStatus,
        DateTimeOffset? ProductionStartedAt,
        DateTimeOffset? ReadyForPickupAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? CancelledAt);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _currentTime;

        internal FixedTimeProvider(DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow() => _currentTime;
    }

    private sealed class FakeApplicationTransaction :
        IApplicationTransaction
    {
        private bool _active;

        public int ExecuteCalls { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);

            if (_active)
            {
                return await operation(cancellationToken);
            }

            ExecuteCalls++;
            _active = true;

            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                _active = false;
            }
        }
    }

    private sealed class FakeJobPaymentCoordination :
        IJobPaymentCoordination
    {
        public Task<bool> LockJobAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> HasBlockingDirectPaymentAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        private readonly Job _job;

        internal FakeJobRepository(Job job)
        {
            _job = job;
        }

        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            Job? result = _job.Id == jobId ? _job : null;
            return Task.FromResult(result);
        }

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            Job job,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The return workflow must not save the Job.");
    }

    private sealed class FakeCreditAccountRepository :
        ICreditAccountRepository
    {
        private readonly CreditAccount _account;

        internal FakeCreditAccountRepository(CreditAccount account)
        {
            _account = account;
        }

        public int SaveCalls { get; private set; }

        public Task<CreditAccount?> FindByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<CreditAccount?>(
                _account.OwnerId == ownerId ? _account : null);

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
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            CreditAccount account,
            CancellationToken cancellationToken)
        {
            Assert.Same(_account, account);
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCreditQueries : ICreditQueries
    {
        private readonly CreditAccount _account;

        internal FakeCreditQueries(CreditAccount account)
        {
            _account = account;
        }

        public Task<CreditMovementListItem?> FindMovementForOwnerAsync(
            Guid ownerId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            if (_account.OwnerId != ownerId)
            {
                return Task.FromResult<CreditMovementListItem?>(null);
            }

            var movement = _account.Movements
                .Select(
                    (item, index) => new CreditMovementListItem(
                        item.OperationId,
                        item.Type,
                        item.Amount.MinorUnits,
                        item.BalanceAfter.MinorUnits,
                        item.Description,
                        item.RecordedAt,
                        index + 1))
                .SingleOrDefault(
                    item => item.OperationId == operationId);

            return Task.FromResult(movement);
        }

        public Task<CreditAdministrationMovementPage>
            ListAdministrationMovementsAsync(
                CreditAdministrationMovementFilter filter,
                CreditMovementPageRequest page,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditMovementPage> ListMovementsForOwnerAsync(
            Guid ownerId,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSettlementReturnRepository :
        ISettlementReturnRepository
    {
        private readonly List<SettlementReturn> _returns = [];

        public int AddCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<SettlementReturn?> FindByIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _returns.SingleOrDefault(
                    item => item.Id == settlementReturnId));

        public Task<SettlementReturn?> FindByRequestIdAsync(
            Guid requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _returns.SingleOrDefault(
                    item => item.RequestId == requestId));

        public Task<SettlementReturn?> FindByOriginalPaymentIdAsync(
            Guid originalPaymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _returns.SingleOrDefault(
                    item =>
                        item.OriginalPaymentId == originalPaymentId));

        public Task<SettlementReturn?> FindByJobIdAsync(
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _returns.SingleOrDefault(item => item.JobId == jobId));

        public Task AddAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default)
        {
            _returns.Add(settlementReturn);
            AddCalls++;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default)
        {
            Assert.Contains(settlementReturn, _returns);
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry)
        {
            Entries.Add(entry);
        }

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
