using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Payments.Domain;

public sealed class SettlementReturn
{
    public const int MaximumReasonLength = 300;

    public SettlementReturn(
        Guid id,
        Guid requestId,
        SettlementReturnKind kind,
        Guid? originalPaymentId,
        Guid? jobId,
        Guid customerUserId,
        Guid administratorUserId,
        Money amount,
        string reason,
        DateTimeOffset requestedAt)
    {
        ValidateId(id, nameof(id));
        ValidateId(requestId, nameof(requestId));
        ValidateId(customerUserId, nameof(customerUserId));
        ValidateId(administratorUserId, nameof(administratorUserId));
        ValidateSource(kind, originalPaymentId, jobId);

        if (amount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Settlement return amount must be positive.");
        }

        var normalizedReason = NormalizeReason(reason);

        if (requestedAt == default)
        {
            throw new ArgumentException(
                "Settlement return request time must not be empty.",
                nameof(requestedAt));
        }

        Id = id;
        RequestId = requestId;
        Kind = kind;
        OriginalPaymentId = originalPaymentId;
        JobId = jobId;
        CustomerUserId = customerUserId;
        AdministratorUserId = administratorUserId;
        Amount = amount;
        Reason = normalizedReason;
        State = SettlementReturnState.Requested;
        RequestedAt = requestedAt;
        UpdatedAt = requestedAt;
    }

    public Guid Id { get; }

    public Guid RequestId { get; }

    public SettlementReturnKind Kind { get; }

    public Guid? OriginalPaymentId { get; }

    public Guid? JobId { get; }

    public Guid CustomerUserId { get; }

    public Guid AdministratorUserId { get; }

    public Money Amount { get; }

    public string Currency => Money.CurrencyCode;

    public string Reason { get; }

    public SettlementReturnState State { get; private set; }

    public DateTimeOffset RequestedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public void Begin(DateTimeOffset startedAt)
    {
        EnsureState(
            SettlementReturnState.Requested,
            SettlementReturnState.InProgress);

        var normalized = ValidateChangedAt(startedAt);
        State = SettlementReturnState.InProgress;
        StartedAt = normalized;
        UpdatedAt = normalized;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureResolutionAllowed(SettlementReturnState.Completed);

        var normalized = ValidateChangedAt(completedAt);
        State = SettlementReturnState.Completed;
        CompletedAt = normalized;
        UpdatedAt = normalized;
    }

    public void Reject(DateTimeOffset rejectedAt)
    {
        EnsureResolutionAllowed(SettlementReturnState.Rejected);

        var normalized = ValidateChangedAt(rejectedAt);
        State = SettlementReturnState.Rejected;
        CompletedAt = normalized;
        UpdatedAt = normalized;
    }

    public void RequireAttention(DateTimeOffset changedAt)
    {
        EnsureState(
            SettlementReturnState.InProgress,
            SettlementReturnState.RequiresAttention);

        var normalized = ValidateChangedAt(changedAt);
        State = SettlementReturnState.RequiresAttention;
        UpdatedAt = normalized;
    }

    internal static SettlementReturn Restore(
        Guid id,
        Guid requestId,
        SettlementReturnKind kind,
        Guid? originalPaymentId,
        Guid? jobId,
        Guid customerUserId,
        Guid administratorUserId,
        Money amount,
        string currency,
        string reason,
        SettlementReturnState state,
        DateTimeOffset requestedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? completedAt)
    {
        if (!string.Equals(
                currency,
                Money.CurrencyCode,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Settlement return '{id}' has unsupported currency.");
        }

        var settlementReturn = new SettlementReturn(
            id,
            requestId,
            kind,
            originalPaymentId,
            jobId,
            customerUserId,
            administratorUserId,
            amount,
            reason,
            requestedAt)
        {
            State = state,
            StartedAt = startedAt,
            UpdatedAt = updatedAt,
            CompletedAt = completedAt
        };

        settlementReturn.ValidatePersistedState();
        return settlementReturn;
    }

    private void EnsureResolutionAllowed(
        SettlementReturnState target)
    {
        if (
            State is not SettlementReturnState.InProgress and
                not SettlementReturnState.RequiresAttention)
        {
            throw new InvalidSettlementReturnStateTransitionException(
                State,
                target);
        }
    }

    private void EnsureState(
        SettlementReturnState required,
        SettlementReturnState target)
    {
        if (State != required)
        {
            throw new InvalidSettlementReturnStateTransitionException(
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
                "Settlement return transition time must be monotonic.");
        }

        return changedAt;
    }

    private void ValidatePersistedState()
    {
        if (
            UpdatedAt < RequestedAt ||
            (StartedAt.HasValue &&
             (StartedAt.Value < RequestedAt ||
              UpdatedAt < StartedAt.Value)) ||
            (CompletedAt.HasValue &&
             (!StartedAt.HasValue ||
              CompletedAt.Value < StartedAt.Value ||
              UpdatedAt < CompletedAt.Value)))
        {
            throw new InvalidDataException(
                $"Settlement return '{Id}' has invalid timestamps.");
        }

        var validShape = State switch
        {
            SettlementReturnState.Requested =>
                StartedAt is null && CompletedAt is null,
            SettlementReturnState.InProgress =>
                StartedAt.HasValue && CompletedAt is null,
            SettlementReturnState.Completed =>
                StartedAt.HasValue && CompletedAt.HasValue,
            SettlementReturnState.Rejected =>
                StartedAt.HasValue && CompletedAt.HasValue,
            SettlementReturnState.RequiresAttention =>
                StartedAt.HasValue && CompletedAt is null,
            _ => false
        };

        if (!validShape)
        {
            throw new InvalidDataException(
                $"Settlement return '{Id}' has an invalid state shape.");
        }
    }

    private static void ValidateSource(
        SettlementReturnKind kind,
        Guid? originalPaymentId,
        Guid? jobId)
    {
        ValidateOptionalId(originalPaymentId, nameof(originalPaymentId));
        ValidateOptionalId(jobId, nameof(jobId));

        var validShape = kind switch
        {
            SettlementReturnKind.CardJob =>
                originalPaymentId.HasValue && jobId.HasValue,
            SettlementReturnKind.CreditJob =>
                originalPaymentId is null && jobId.HasValue,
            SettlementReturnKind.CardTopUp =>
                originalPaymentId.HasValue && jobId is null,
            _ => false
        };

        if (!validShape)
        {
            throw new ArgumentException(
                "Settlement return source does not match its kind.",
                nameof(kind));
        }
    }

    private static string NormalizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Settlement return reason must not be blank.",
                nameof(reason));
        }

        var normalized = reason.Trim();

        if (normalized.Length > MaximumReasonLength)
        {
            throw new ArgumentException(
                "Settlement return reason is too long.",
                nameof(reason));
        }

        return normalized;
    }

    private static void ValidateOptionalId(
        Guid? value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Settlement return source ID must not be empty.",
                parameterName);
        }
    }

    private static void ValidateId(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Settlement return ID must not be empty.",
                parameterName);
        }
    }
}
