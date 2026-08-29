using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Domain;

public sealed class SettlementReturnProviderAttemptTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_CreatesNormalizedPreparedAttempt()
    {
        var attempt = Create(providerReference: "  provider-reference  ");

        Assert.Equal("provider-reference", attempt.ProviderReference);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Prepared,
            attempt.State);
        Assert.True(attempt.IsActive);
        Assert.Equal(CreatedAt, attempt.CreatedAt);
        Assert.Equal(CreatedAt, attempt.UpdatedAt);
        Assert.Null(attempt.StartedAt);
        Assert.Null(attempt.FinishedAt);
        Assert.Null(attempt.Diagnostic);
    }

    [Fact]
    public void Constructor_RejectsInvalidIdsProviderOperationAndReference()
    {
        Assert.Throws<ArgumentException>(() => Create(id: Guid.Empty));
        Assert.Throws<ArgumentException>(
            () => Create(settlementReturnId: Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(provider: PaymentProvider.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(
                operation: SettlementReturnProviderOperation.Unknown));
        Assert.Throws<ArgumentException>(
            () => Create(providerReference: "   "));
        Assert.Throws<ArgumentException>(
            () => Create(
                providerReference: new string(
                    'x',
                    PaymentProviderReference.MaxLength + 1)));
    }

    [Fact]
    public void BeginAndConfirm_ProduceTerminalMonotonicLifecycle()
    {
        var attempt = Create();
        var startedAt = CreatedAt.AddMinutes(1);
        var confirmedAt = CreatedAt.AddMinutes(2);

        attempt.Begin(startedAt);
        attempt.Confirm(confirmedAt);

        Assert.Equal(
            SettlementReturnProviderAttemptState.Confirmed,
            attempt.State);
        Assert.False(attempt.IsActive);
        Assert.Equal(startedAt, attempt.StartedAt);
        Assert.Equal(confirmedAt, attempt.FinishedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void RejectPrepared_RecordsDefinitelyUnsentOutcome()
    {
        var attempt = Create();
        var rejectedAt = CreatedAt.AddMinutes(1);

        attempt.Reject("  preflight rejected  ", rejectedAt);

        Assert.Equal(
            SettlementReturnProviderAttemptState.Rejected,
            attempt.State);
        Assert.False(attempt.IsActive);
        Assert.Null(attempt.StartedAt);
        Assert.Equal(rejectedAt, attempt.FinishedAt);
        Assert.Equal("preflight rejected", attempt.Diagnostic);
    }

    [Fact]
    public void MarkUncertain_RemainsActiveAndCannotRestart()
    {
        var attempt = CreateInProgress();
        var uncertainAt = CreatedAt.AddMinutes(2);

        attempt.MarkUncertain("connection timeout", uncertainAt);

        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            attempt.State);
        Assert.True(attempt.IsActive);
        Assert.Null(attempt.FinishedAt);
        Assert.Throws<
            InvalidSettlementReturnProviderAttemptStateTransitionException>(
                () => attempt.Begin(uncertainAt.AddMinutes(1)));
    }

    [Fact]
    public void Uncertain_CanOnlyBeResolvedToTerminalOutcome()
    {
        var confirmed = CreateUncertain();
        var rejected = CreateUncertain();

        confirmed.Confirm(CreatedAt.AddMinutes(3));
        rejected.Reject(
            "operator verified rejection",
            CreatedAt.AddMinutes(3));

        Assert.Equal(
            SettlementReturnProviderAttemptState.Confirmed,
            confirmed.State);
        Assert.Equal("connection timeout", confirmed.Diagnostic);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Rejected,
            rejected.State);
        Assert.Equal("operator verified rejection", rejected.Diagnostic);
    }

    [Fact]
    public void Diagnostic_IsTrimmedAndBounded()
    {
        var attempt = CreateInProgress();
        var diagnostic =
            "  " +
            new string(
                'x',
                SettlementReturnProviderAttempt.MaximumDiagnosticLength + 20) +
            "  ";

        attempt.MarkUncertain(diagnostic, CreatedAt.AddMinutes(2));

        Assert.Equal(
            SettlementReturnProviderAttempt.MaximumDiagnosticLength,
            attempt.Diagnostic!.Length);
        Assert.DoesNotContain(' ', attempt.Diagnostic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Diagnostic_MustNotBeBlank(string diagnostic)
    {
        var inProgress = CreateInProgress();
        var prepared = Create();

        Assert.Throws<ArgumentException>(
            () => inProgress.MarkUncertain(
                diagnostic,
                CreatedAt.AddMinutes(2)));
        Assert.Throws<ArgumentException>(
            () => prepared.Reject(
                diagnostic,
                CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void TransitionTimes_MustBeMonotonic()
    {
        var attempt = CreateInProgress();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => attempt.MarkUncertain("timeout", CreatedAt));
    }

    [Fact]
    public void InvalidTransitions_AreRejected()
    {
        var prepared = Create();
        var confirmed = CreateInProgress();
        confirmed.Confirm(CreatedAt.AddMinutes(2));

        Assert.Throws<
            InvalidSettlementReturnProviderAttemptStateTransitionException>(
                () => prepared.Confirm(CreatedAt.AddMinutes(1)));
        Assert.Throws<
            InvalidSettlementReturnProviderAttemptStateTransitionException>(
                () => prepared.MarkUncertain(
                    "timeout",
                    CreatedAt.AddMinutes(1)));
        Assert.Throws<
            InvalidSettlementReturnProviderAttemptStateTransitionException>(
                () => confirmed.Reject(
                    "late rejection",
                    CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void ImmutableIdentityAndOperation_HaveNoPublicMutationPath()
    {
        string[] immutableProperties =
        [
            nameof(SettlementReturnProviderAttempt.Id),
            nameof(SettlementReturnProviderAttempt.SettlementReturnId),
            nameof(SettlementReturnProviderAttempt.Provider),
            nameof(SettlementReturnProviderAttempt.Operation),
            nameof(SettlementReturnProviderAttempt.ProviderReference),
            nameof(SettlementReturnProviderAttempt.CreatedAt)
        ];

        foreach (var propertyName in immutableProperties)
        {
            var property = typeof(SettlementReturnProviderAttempt)
                .GetProperty(propertyName)!;

            Assert.False(property.SetMethod?.IsPublic ?? false);
        }
    }

    [Theory]
    [InlineData(SettlementReturnProviderAttemptState.Prepared, true, false)]
    [InlineData(SettlementReturnProviderAttemptState.InProgress, false, false)]
    [InlineData(SettlementReturnProviderAttemptState.Confirmed, true, true)]
    [InlineData(SettlementReturnProviderAttemptState.Uncertain, true, true)]
    public void Restore_InvalidPersistedShapesFailClosed(
        SettlementReturnProviderAttemptState state,
        bool omitStartedAt,
        bool omitDiagnostic)
    {
        Assert.Throws<InvalidDataException>(
            () => SettlementReturnProviderAttempt.Restore(
                Guid.NewGuid(),
                Guid.NewGuid(),
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                "provider-reference",
                state,
                omitDiagnostic ? null : "diagnostic",
                CreatedAt,
                CreatedAt.AddMinutes(2),
                omitStartedAt ? null : CreatedAt.AddMinutes(1),
                state == SettlementReturnProviderAttemptState.Confirmed
                    ? CreatedAt.AddMinutes(2)
                    : null));
    }

    [Fact]
    public void Restore_NonNormalizedPersistedValuesFailClosed()
    {
        Assert.Throws<InvalidDataException>(
            () => SettlementReturnProviderAttempt.Restore(
                Guid.NewGuid(),
                Guid.NewGuid(),
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                " provider-reference ",
                SettlementReturnProviderAttemptState.Prepared,
                diagnostic: null,
                CreatedAt,
                CreatedAt,
                startedAt: null,
                finishedAt: null));

        Assert.Throws<InvalidDataException>(
            () => SettlementReturnProviderAttempt.Restore(
                Guid.NewGuid(),
                Guid.NewGuid(),
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                "provider-reference",
                SettlementReturnProviderAttemptState.Uncertain,
                " diagnostic ",
                CreatedAt,
                CreatedAt.AddMinutes(2),
                CreatedAt.AddMinutes(1),
                finishedAt: null));
    }

    private static SettlementReturnProviderAttempt CreateInProgress()
    {
        var attempt = Create();
        attempt.Begin(CreatedAt.AddMinutes(1));
        return attempt;
    }

    private static SettlementReturnProviderAttempt CreateUncertain()
    {
        var attempt = CreateInProgress();
        attempt.MarkUncertain(
            "connection timeout",
            CreatedAt.AddMinutes(2));
        return attempt;
    }

    private static SettlementReturnProviderAttempt Create(
        Guid? id = null,
        Guid? settlementReturnId = null,
        PaymentProvider provider = PaymentProvider.Csob,
        SettlementReturnProviderOperation operation =
            SettlementReturnProviderOperation.Refund,
        string providerReference = "provider-reference")
    {
        return new SettlementReturnProviderAttempt(
            id ?? Guid.NewGuid(),
            settlementReturnId ?? Guid.NewGuid(),
            provider,
            operation,
            providerReference,
            CreatedAt);
    }
}
