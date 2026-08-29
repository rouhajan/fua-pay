using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Application;

public sealed class SettlementReturnRegistrationServiceTests
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisterAsync_NewAuthoritativeReturn_AddsIt()
    {
        var repository = new FakeRepository();
        var service = new SettlementReturnRegistrationService(repository);
        var candidate = Create();

        var result = await service.RegisterAsync(candidate);

        Assert.True(result.Created);
        Assert.Same(candidate, result.SettlementReturn);
        Assert.Same(candidate, repository.Stored);
    }

    [Fact]
    public async Task RegisterAsync_SameRequestAndPayload_ReturnsExisting()
    {
        var requestId = Guid.NewGuid();
        var originalPaymentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var existing = Create(
            requestId,
            originalPaymentId,
            jobId);
        existing.Begin(RequestedAt.AddMinutes(1));
        var candidate = CreateMatching(existing);
        var repository = new FakeRepository { Stored = existing };
        var service = new SettlementReturnRegistrationService(repository);

        var result = await service.RegisterAsync(candidate);

        Assert.False(result.Created);
        Assert.Same(existing, result.SettlementReturn);
        Assert.Equal(0, repository.AddCalls);
    }

    [Fact]
    public async Task RegisterAsync_ConflictingRequestPayload_RejectsReplay()
    {
        var requestId = Guid.NewGuid();
        var existing = Create(requestId: requestId);
        var candidate = CreateMatching(
            existing,
            reason: "Different reason");
        var repository = new FakeRepository { Stored = existing };
        var service = new SettlementReturnRegistrationService(repository);

        var exception = await Assert.ThrowsAsync<
            SettlementReturnRequestConflictException>(
                () => service.RegisterAsync(candidate));

        Assert.Equal(requestId, exception.RequestId);
        Assert.Equal(0, repository.AddCalls);
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentSameRequest_VerifiesPayload()
    {
        var requestId = Guid.NewGuid();
        var originalPaymentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var existing = Create(
            requestId,
            originalPaymentId,
            jobId);
        var candidate = CreateMatching(existing);
        var repository = new FakeRepository
        {
            StoredAfterAddFailure = existing,
            AddException =
                new SettlementReturnRequestAlreadyExistsException(
                    requestId)
        };
        var service = new SettlementReturnRegistrationService(repository);

        var result = await service.RegisterAsync(candidate);

        Assert.False(result.Created);
        Assert.Same(existing, result.SettlementReturn);
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentConflictingRequest_RejectsReplay()
    {
        var requestId = Guid.NewGuid();
        var existing = Create(
            requestId: requestId,
            reason: "Original reason");
        var candidate = CreateMatching(
            existing,
            reason: "Conflicting reason");
        var repository = new FakeRepository
        {
            StoredAfterAddFailure = existing,
            AddException =
                new SettlementReturnRequestAlreadyExistsException(
                    requestId)
        };
        var service = new SettlementReturnRegistrationService(repository);

        await Assert.ThrowsAsync<SettlementReturnRequestConflictException>(
            () => service.RegisterAsync(candidate));
    }

    [Fact]
    public async Task RegisterAsync_DifferentRequestForOriginalPayment_IdentifiesExisting()
    {
        var originalPaymentId = Guid.NewGuid();
        var existing = Create(originalPaymentId: originalPaymentId);
        var candidate = Create(originalPaymentId: originalPaymentId);
        var repository = new FakeRepository
        {
            StoredAfterAddFailure = existing,
            AddException =
                new SettlementReturnOriginalPaymentAlreadyExistsException(
                    originalPaymentId)
        };
        var service = new SettlementReturnRegistrationService(repository);

        var exception = await Assert.ThrowsAsync<
            SettlementReturnSourceConflictException>(
                () => service.RegisterAsync(candidate));

        Assert.Equal(existing.Id, exception.ExistingSettlementReturnId);
        Assert.Equal(candidate.RequestId, exception.RequestId);
    }

    [Fact]
    public async Task RegisterAsync_DifferentRequestForJob_IdentifiesExisting()
    {
        var jobId = Guid.NewGuid();
        var existing = Create(jobId: jobId);
        var candidate = Create(jobId: jobId);
        var repository = new FakeRepository
        {
            StoredAfterAddFailure = existing,
            AddException =
                new SettlementReturnJobAlreadyExistsException(jobId)
        };
        var service = new SettlementReturnRegistrationService(repository);

        var exception = await Assert.ThrowsAsync<
            SettlementReturnSourceConflictException>(
                () => service.RegisterAsync(candidate));

        Assert.Equal(existing.Id, exception.ExistingSettlementReturnId);
    }

    [Fact]
    public async Task RegisterAsync_ProgressedCandidate_IsRejected()
    {
        var repository = new FakeRepository();
        var service = new SettlementReturnRegistrationService(repository);
        var candidate = Create();
        candidate.Begin(RequestedAt.AddMinutes(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegisterAsync(candidate));

        Assert.Equal(0, repository.AddCalls);
    }

    private static SettlementReturn Create(
        Guid? requestId = null,
        Guid? originalPaymentId = null,
        Guid? jobId = null,
        string reason = "Administrative reason")
    {
        return new SettlementReturn(
            Guid.NewGuid(),
            requestId ?? Guid.NewGuid(),
            SettlementReturnKind.CardJob,
            originalPaymentId ?? Guid.NewGuid(),
            jobId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(12_345),
            reason,
            RequestedAt);
    }

    private static SettlementReturn CreateMatching(
        SettlementReturn existing,
        string? reason = null)
    {
        return new SettlementReturn(
            Guid.NewGuid(),
            existing.RequestId,
            existing.Kind,
            existing.OriginalPaymentId,
            existing.JobId,
            existing.CustomerUserId,
            existing.AdministratorUserId,
            existing.Amount,
            reason ?? existing.Reason,
            RequestedAt.AddSeconds(30));
    }

    private sealed class FakeRepository : ISettlementReturnRepository
    {
        public SettlementReturn? Stored { get; set; }

        public SettlementReturn? StoredAfterAddFailure { get; set; }

        public Exception? AddException { get; set; }

        public int AddCalls { get; private set; }

        public Task<SettlementReturn?> FindByIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Stored?.Id == settlementReturnId ? Stored : null);

        public Task<SettlementReturn?> FindByRequestIdAsync(
            Guid requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Current?.RequestId == requestId ? Current : null);

        public Task<SettlementReturn?> FindByOriginalPaymentIdAsync(
            Guid originalPaymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Current?.OriginalPaymentId == originalPaymentId
                    ? Current
                    : null);

        public Task<SettlementReturn?> FindByJobIdAsync(
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Current?.JobId == jobId ? Current : null);

        public Task AddAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default)
        {
            AddCalls++;

            if (AddException is not null)
            {
                throw AddException;
            }

            Stored = settlementReturn;
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default)
        {
            Stored = settlementReturn;
            return Task.CompletedTask;
        }

        private SettlementReturn? Current =>
            AddCalls > 0 && StoredAfterAddFailure is not null
                ? StoredAfterAddFailure
                : Stored;
    }
}
