namespace FuaPay.Web.Modules.Payments.Domain;

public sealed class SettlementReturnProviderAttempt
{
    public const int MaximumDiagnosticLength = 500;

    public SettlementReturnProviderAttempt(
        Guid id,
        Guid settlementReturnId,
        PaymentProvider provider,
        SettlementReturnProviderOperation operation,
        string providerReference,
        DateTimeOffset createdAt)
    {
        ValidateId(id, nameof(id));
        ValidateId(settlementReturnId, nameof(settlementReturnId));
        ValidateProvider(provider);
        ValidateOperation(operation);

        if (createdAt == default)
        {
            throw new ArgumentException(
                "Provider attempt creation time must not be empty.",
                nameof(createdAt));
        }

        Id = id;
        SettlementReturnId = settlementReturnId;
        Provider = provider;
        Operation = operation;
        ProviderReference = PaymentProviderReference.Normalize(
            providerReference,
            nameof(providerReference));
        State = SettlementReturnProviderAttemptState.Prepared;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid SettlementReturnId { get; }

    public PaymentProvider Provider { get; }

    public SettlementReturnProviderOperation Operation { get; }

    public string ProviderReference { get; }

    public SettlementReturnProviderAttemptState State { get; private set; }

    public string? Diagnostic { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public bool IsActive => State is
        SettlementReturnProviderAttemptState.Prepared or
        SettlementReturnProviderAttemptState.InProgress or
        SettlementReturnProviderAttemptState.Uncertain;

    public void Begin(DateTimeOffset startedAt)
    {
        EnsureState(
            SettlementReturnProviderAttemptState.Prepared,
            SettlementReturnProviderAttemptState.InProgress);

        var normalized = ValidateChangedAt(startedAt);
        State = SettlementReturnProviderAttemptState.InProgress;
        StartedAt = normalized;
        UpdatedAt = normalized;
    }

    public void Confirm(DateTimeOffset confirmedAt)
    {
        EnsureResolutionAllowed(
            SettlementReturnProviderAttemptState.Confirmed);

        var normalized = ValidateChangedAt(confirmedAt);
        State = SettlementReturnProviderAttemptState.Confirmed;
        FinishedAt = normalized;
        UpdatedAt = normalized;
    }

    public void Reject(
        string diagnostic,
        DateTimeOffset rejectedAt)
    {
        if (State is not
            SettlementReturnProviderAttemptState.Prepared and not
            SettlementReturnProviderAttemptState.InProgress and not
            SettlementReturnProviderAttemptState.Uncertain)
        {
            throw new InvalidSettlementReturnProviderAttemptStateTransitionException(
                State,
                SettlementReturnProviderAttemptState.Rejected);
        }

        var normalizedDiagnostic = NormalizeDiagnostic(diagnostic);
        var normalizedTime = ValidateChangedAt(rejectedAt);
        State = SettlementReturnProviderAttemptState.Rejected;
        Diagnostic = normalizedDiagnostic;
        FinishedAt = normalizedTime;
        UpdatedAt = normalizedTime;
    }

    public void MarkUncertain(
        string diagnostic,
        DateTimeOffset changedAt)
    {
        EnsureState(
            SettlementReturnProviderAttemptState.InProgress,
            SettlementReturnProviderAttemptState.Uncertain);

        var normalizedDiagnostic = NormalizeDiagnostic(diagnostic);
        var normalizedTime = ValidateChangedAt(changedAt);
        State = SettlementReturnProviderAttemptState.Uncertain;
        Diagnostic = normalizedDiagnostic;
        UpdatedAt = normalizedTime;
    }

    internal static SettlementReturnProviderAttempt Restore(
        Guid id,
        Guid settlementReturnId,
        PaymentProvider provider,
        SettlementReturnProviderOperation operation,
        string providerReference,
        SettlementReturnProviderAttemptState state,
        string? diagnostic,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? finishedAt)
    {
        var attempt = new SettlementReturnProviderAttempt(
            id,
            settlementReturnId,
            provider,
            operation,
            providerReference,
            createdAt)
        {
            State = state,
            Diagnostic = diagnostic,
            UpdatedAt = updatedAt,
            StartedAt = startedAt,
            FinishedAt = finishedAt
        };

        if (!string.Equals(
                attempt.ProviderReference,
                providerReference,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Settlement return provider attempt '{id}' has a " +
                "non-normalized provider reference.");
        }

        attempt.ValidatePersistedState();
        return attempt;
    }

    private void EnsureResolutionAllowed(
        SettlementReturnProviderAttemptState target)
    {
        if (State is not
            SettlementReturnProviderAttemptState.InProgress and not
            SettlementReturnProviderAttemptState.Uncertain)
        {
            throw new InvalidSettlementReturnProviderAttemptStateTransitionException(
                State,
                target);
        }
    }

    private void EnsureState(
        SettlementReturnProviderAttemptState required,
        SettlementReturnProviderAttemptState target)
    {
        if (State != required)
        {
            throw new InvalidSettlementReturnProviderAttemptStateTransitionException(
                State,
                target);
        }
    }

    private DateTimeOffset ValidateChangedAt(
        DateTimeOffset changedAt)
    {
        if (changedAt == default || changedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(changedAt),
                "Provider attempt transition time must be monotonic.");
        }

        return changedAt;
    }

    private void ValidatePersistedState()
    {
        if (!Enum.IsDefined(State) || State ==
            SettlementReturnProviderAttemptState.Unknown)
        {
            throw new InvalidDataException(
                $"Settlement return provider attempt '{Id}' has an " +
                "unsupported state.");
        }

        if (
            UpdatedAt < CreatedAt ||
            (StartedAt.HasValue && StartedAt.Value < CreatedAt) ||
            (FinishedAt.HasValue &&
             (FinishedAt.Value < CreatedAt ||
              (StartedAt.HasValue &&
               FinishedAt.Value < StartedAt.Value))) ||
            (StartedAt.HasValue && UpdatedAt < StartedAt.Value) ||
            (FinishedAt.HasValue && UpdatedAt < FinishedAt.Value))
        {
            throw new InvalidDataException(
                $"Settlement return provider attempt '{Id}' has invalid " +
                "timestamps.");
        }

        var validShape = State switch
        {
            SettlementReturnProviderAttemptState.Prepared =>
                StartedAt is null &&
                FinishedAt is null &&
                UpdatedAt == CreatedAt,
            SettlementReturnProviderAttemptState.InProgress =>
                StartedAt.HasValue &&
                FinishedAt is null &&
                UpdatedAt == StartedAt.Value,
            SettlementReturnProviderAttemptState.Confirmed =>
                StartedAt.HasValue &&
                FinishedAt.HasValue &&
                UpdatedAt == FinishedAt.Value,
            SettlementReturnProviderAttemptState.Rejected =>
                FinishedAt.HasValue &&
                UpdatedAt == FinishedAt.Value,
            SettlementReturnProviderAttemptState.Uncertain =>
                StartedAt.HasValue &&
                FinishedAt is null &&
                UpdatedAt >= StartedAt.Value,
            _ => false
        };

        if (!validShape)
        {
            throw new InvalidDataException(
                $"Settlement return provider attempt '{Id}' has an " +
                "invalid state shape.");
        }

        if (Diagnostic is not null)
        {
            var normalized = NormalizeDiagnostic(Diagnostic);

            if (!string.Equals(
                    normalized,
                    Diagnostic,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Settlement return provider attempt '{Id}' has a " +
                    "non-normalized diagnostic.");
            }
        }

        if (
            State is SettlementReturnProviderAttemptState.Rejected or
                SettlementReturnProviderAttemptState.Uncertain)
        {
            if (Diagnostic is null)
            {
                throw new InvalidDataException(
                    $"Settlement return provider attempt '{Id}' has no " +
                    "required diagnostic.");
            }
        }
        else if (
            (State is SettlementReturnProviderAttemptState.Prepared or
                SettlementReturnProviderAttemptState.InProgress) &&
            Diagnostic is not null)
        {
            throw new InvalidDataException(
                $"Settlement return provider attempt '{Id}' has an " +
                "unexpected diagnostic.");
        }
    }

    private static string NormalizeDiagnostic(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            throw new ArgumentException(
                "Provider attempt diagnostic must not be blank.",
                nameof(diagnostic));
        }

        var normalized = diagnostic.Trim();

        return normalized.Length <= MaximumDiagnosticLength
            ? normalized
            : normalized[..MaximumDiagnosticLength];
    }

    private static void ValidateProvider(PaymentProvider provider)
    {
        if (provider == PaymentProvider.Unknown || !Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
    }

    private static void ValidateOperation(
        SettlementReturnProviderOperation operation)
    {
        if (
            operation == SettlementReturnProviderOperation.Unknown ||
            !Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Provider attempt ID must not be empty.",
                parameterName);
        }
    }
}
