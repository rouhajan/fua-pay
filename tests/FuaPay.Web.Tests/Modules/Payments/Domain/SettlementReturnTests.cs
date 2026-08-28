using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Domain;

public sealed class SettlementReturnTests
{
    private static readonly DateTimeOffset RequestedAt =
        new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_AcceptsCardJobSource()
    {
        var originalPaymentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var settlementReturn = Create(
            SettlementReturnKind.CardJob,
            originalPaymentId,
            jobId);

        Assert.Equal(originalPaymentId, settlementReturn.OriginalPaymentId);
        Assert.Equal(jobId, settlementReturn.JobId);
    }

    [Fact]
    public void Constructor_AcceptsCreditJobSource()
    {
        var jobId = Guid.NewGuid();

        var settlementReturn = Create(
            SettlementReturnKind.CreditJob,
            originalPaymentId: null,
            jobId);

        Assert.Null(settlementReturn.OriginalPaymentId);
        Assert.Equal(jobId, settlementReturn.JobId);
    }

    [Fact]
    public void Constructor_AcceptsCardTopUpSource()
    {
        var originalPaymentId = Guid.NewGuid();

        var settlementReturn = Create(
            SettlementReturnKind.CardTopUp,
            originalPaymentId,
            jobId: null);

        Assert.Equal(originalPaymentId, settlementReturn.OriginalPaymentId);
        Assert.Null(settlementReturn.JobId);
    }

    [Theory]
    [InlineData(SettlementReturnKind.Unknown, false, false)]
    [InlineData(SettlementReturnKind.CardJob, false, true)]
    [InlineData(SettlementReturnKind.CardJob, true, false)]
    [InlineData(SettlementReturnKind.CreditJob, true, true)]
    [InlineData(SettlementReturnKind.CreditJob, false, false)]
    [InlineData(SettlementReturnKind.CardTopUp, false, false)]
    [InlineData(SettlementReturnKind.CardTopUp, true, true)]
    public void Constructor_RejectsInvalidSourceShapes(
        SettlementReturnKind kind,
        bool hasOriginalPayment,
        bool hasJob)
    {
        Assert.Throws<ArgumentException>(
            () => new SettlementReturn(
                Guid.NewGuid(),
                Guid.NewGuid(),
                kind,
                hasOriginalPayment ? Guid.NewGuid() : null,
                hasJob ? Guid.NewGuid() : null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                new Money(12_345),
                "Administrative reason",
                RequestedAt));
    }

    [Fact]
    public void Constructor_RejectsEmptySourceIds()
    {
        Assert.Throws<ArgumentException>(
            () => Create(
                SettlementReturnKind.CardJob,
                Guid.Empty,
                Guid.NewGuid()));
        Assert.Throws<ArgumentException>(
            () => Create(
                SettlementReturnKind.CardJob,
                Guid.NewGuid(),
                Guid.Empty));
    }

    [Fact]
    public void Constructor_RejectsEmptyRequestId()
    {
        Assert.Throws<ArgumentException>(
            () => Create(requestId: Guid.Empty));
    }

    [Fact]
    public void Constructor_RejectsEmptyCustomerUserId()
    {
        Assert.Throws<ArgumentException>(
            () => Create(customerUserId: Guid.Empty));
    }

    [Fact]
    public void Constructor_RejectsEmptyAdministratorUserId()
    {
        Assert.Throws<ArgumentException>(
            () => Create(administratorUserId: Guid.Empty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveAmount(long minorUnits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(amount: new Money(minorUnits)));
    }

    [Fact]
    public void Currency_IsStructurallyFixedToCzkMoneyModel()
    {
        var settlementReturn = Create();

        Assert.Equal("CZK", Money.CurrencyCode);
        Assert.Equal(Money.CurrencyCode, settlementReturn.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankReason(string reason)
    {
        Assert.Throws<ArgumentException>(
            () => Create(reason: reason));
    }

    [Fact]
    public void Constructor_RejectsReasonBeyondMaximumLength()
    {
        Assert.Throws<ArgumentException>(
            () => Create(
                reason: new string(
                    'x',
                    SettlementReturn.MaximumReasonLength + 1)));
    }

    [Fact]
    public void Constructor_NormalizesReasonAndStartsRequested()
    {
        var settlementReturn = Create(reason: "  Administrative reason  ");

        Assert.Equal("Administrative reason", settlementReturn.Reason);
        Assert.Equal(SettlementReturnState.Requested, settlementReturn.State);
        Assert.Equal(RequestedAt, settlementReturn.RequestedAt);
        Assert.Equal(RequestedAt, settlementReturn.UpdatedAt);
        Assert.Null(settlementReturn.StartedAt);
        Assert.Null(settlementReturn.CompletedAt);
    }

    [Fact]
    public void Begin_TransitionsRequestedToInProgress()
    {
        var settlementReturn = Create();
        var startedAt = RequestedAt.AddMinutes(1);

        settlementReturn.Begin(startedAt);

        Assert.Equal(
            SettlementReturnState.InProgress,
            settlementReturn.State);
        Assert.Equal(startedAt, settlementReturn.StartedAt);
        Assert.Equal(startedAt, settlementReturn.UpdatedAt);
        Assert.Null(settlementReturn.CompletedAt);
    }

    [Fact]
    public void Complete_TransitionsInProgressToCompleted()
    {
        var settlementReturn = CreateInProgress();
        var completedAt = RequestedAt.AddMinutes(2);

        settlementReturn.Complete(completedAt);

        Assert.Equal(
            SettlementReturnState.Completed,
            settlementReturn.State);
        Assert.Equal(completedAt, settlementReturn.CompletedAt);
        Assert.Equal(completedAt, settlementReturn.UpdatedAt);
    }

    [Fact]
    public void Reject_TransitionsInProgressToRejected()
    {
        var settlementReturn = CreateInProgress();
        var rejectedAt = RequestedAt.AddMinutes(2);

        settlementReturn.Reject(rejectedAt);

        Assert.Equal(
            SettlementReturnState.Rejected,
            settlementReturn.State);
        Assert.Equal(rejectedAt, settlementReturn.CompletedAt);
    }

    [Fact]
    public void RequireAttention_TransitionsOnlyFromInProgress()
    {
        var settlementReturn = CreateInProgress();
        var changedAt = RequestedAt.AddMinutes(2);

        settlementReturn.RequireAttention(changedAt);

        Assert.Equal(
            SettlementReturnState.RequiresAttention,
            settlementReturn.State);
        Assert.Equal(changedAt, settlementReturn.UpdatedAt);
        Assert.Null(settlementReturn.CompletedAt);
    }

    [Fact]
    public void Complete_TransitionsRequiresAttentionToCompleted()
    {
        var settlementReturn = CreateRequiresAttention();
        var completedAt = RequestedAt.AddMinutes(3);

        settlementReturn.Complete(completedAt);

        Assert.Equal(
            SettlementReturnState.Completed,
            settlementReturn.State);
        Assert.Equal(completedAt, settlementReturn.CompletedAt);
    }

    [Fact]
    public void Reject_TransitionsRequiresAttentionToRejected()
    {
        var settlementReturn = CreateRequiresAttention();
        var rejectedAt = RequestedAt.AddMinutes(3);

        settlementReturn.Reject(rejectedAt);

        Assert.Equal(
            SettlementReturnState.Rejected,
            settlementReturn.State);
        Assert.Equal(rejectedAt, settlementReturn.CompletedAt);
    }

    [Fact]
    public void InvalidTransitions_AreRejected()
    {
        var requested = Create();
        var inProgress = CreateInProgress();
        var requiresAttention = CreateRequiresAttention();

        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => requested.Complete(RequestedAt.AddMinutes(1)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => requested.Reject(RequestedAt.AddMinutes(1)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => requested.RequireAttention(RequestedAt.AddMinutes(1)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => inProgress.Begin(RequestedAt.AddMinutes(2)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => requiresAttention.Begin(RequestedAt.AddMinutes(3)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => requiresAttention.RequireAttention(
                RequestedAt.AddMinutes(3)));
    }

    [Fact]
    public void Completed_CannotTransitionAgain()
    {
        var settlementReturn = CreateInProgress();
        settlementReturn.Complete(RequestedAt.AddMinutes(2));

        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => settlementReturn.Begin(RequestedAt.AddMinutes(3)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => settlementReturn.Complete(RequestedAt.AddMinutes(3)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => settlementReturn.Reject(RequestedAt.AddMinutes(3)));
        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => settlementReturn.RequireAttention(
                RequestedAt.AddMinutes(3)));
    }

    [Fact]
    public void Rejected_CannotRestart()
    {
        var settlementReturn = CreateInProgress();
        settlementReturn.Reject(RequestedAt.AddMinutes(2));

        Assert.Throws<InvalidSettlementReturnStateTransitionException>(
            () => settlementReturn.Begin(RequestedAt.AddMinutes(3)));
    }

    [Fact]
    public void TransitionTimes_MustBeMonotonic()
    {
        var settlementReturn = CreateInProgress();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => settlementReturn.Complete(RequestedAt));
    }

    [Fact]
    public void AuthoritativeRequestData_HasNoPublicMutationPath()
    {
        string[] immutableProperties =
        [
            nameof(SettlementReturn.Id),
            nameof(SettlementReturn.RequestId),
            nameof(SettlementReturn.Kind),
            nameof(SettlementReturn.OriginalPaymentId),
            nameof(SettlementReturn.JobId),
            nameof(SettlementReturn.CustomerUserId),
            nameof(SettlementReturn.AdministratorUserId),
            nameof(SettlementReturn.Amount),
            nameof(SettlementReturn.Currency),
            nameof(SettlementReturn.Reason),
            nameof(SettlementReturn.RequestedAt)
        ];

        foreach (var propertyName in immutableProperties)
        {
            var property = typeof(SettlementReturn).GetProperty(
                propertyName)!;

            Assert.False(property.SetMethod?.IsPublic ?? false);
        }

        Assert.False(
            typeof(SettlementReturn)
                .GetProperty(nameof(SettlementReturn.State))!
                .SetMethod!
                .IsPublic);
    }

    private static SettlementReturn CreateInProgress()
    {
        var settlementReturn = Create();
        settlementReturn.Begin(RequestedAt.AddMinutes(1));
        return settlementReturn;
    }

    private static SettlementReturn CreateRequiresAttention()
    {
        var settlementReturn = CreateInProgress();
        settlementReturn.RequireAttention(RequestedAt.AddMinutes(2));
        return settlementReturn;
    }

    private static SettlementReturn Create(
        SettlementReturnKind kind = SettlementReturnKind.CardJob,
        Guid? originalPaymentId = null,
        Guid? jobId = null,
        Guid? requestId = null,
        Guid? customerUserId = null,
        Guid? administratorUserId = null,
        Money? amount = null,
        string reason = "Administrative reason")
    {
        return new SettlementReturn(
            Guid.NewGuid(),
            requestId ?? Guid.NewGuid(),
            kind,
            originalPaymentId ??
                (kind is SettlementReturnKind.CardJob or
                    SettlementReturnKind.CardTopUp
                    ? Guid.NewGuid()
                    : null),
            jobId ??
                (kind is SettlementReturnKind.CardJob or
                    SettlementReturnKind.CreditJob
                    ? Guid.NewGuid()
                    : null),
            customerUserId ?? Guid.NewGuid(),
            administratorUserId ?? Guid.NewGuid(),
            amount ?? new Money(12_345),
            reason,
            RequestedAt);
    }
}
