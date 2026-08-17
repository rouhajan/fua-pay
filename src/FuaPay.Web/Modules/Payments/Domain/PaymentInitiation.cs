namespace FuaPay.Web.Modules.Payments.Domain;

public sealed class PaymentInitiation
{
    public const long MaximumOrderNumber = 9_999_999_999L;
    public const int MaximumErrorLength = 500;
    public const int MaximumProcessUriLength = 2048;

    public PaymentInitiation(
        Guid paymentId,
        PaymentProvider provider,
        long orderNumber,
        Guid correlationId,
        DateTimeOffset createdAt)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID platby nesmí být prázdné.",
                nameof(paymentId));
        }

        if (
            provider == PaymentProvider.Unknown ||
            !Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        if (orderNumber is < 1 or > MaximumOrderNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(orderNumber));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Korelační ID nesmí být prázdné.",
                nameof(correlationId));
        }

        if (createdAt == default)
        {
            throw new ArgumentException(
                "Čas přípravy inicializace nesmí být prázdný.",
                nameof(createdAt));
        }

        PaymentId = paymentId;
        Provider = provider;
        OrderNumber = orderNumber;
        CorrelationId = correlationId;
        State = PaymentInitiationState.Prepared;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid PaymentId { get; }

    public PaymentProvider Provider { get; }

    public long OrderNumber { get; }

    public Guid CorrelationId { get; }

    public string CorrelationData =>
        PaymentProviderCorrelation.Encode(
            PaymentId,
            CorrelationId);

    public PaymentInitiationState State { get; private set; }

    public string? LastError { get; private set; }

    public Uri? ProcessUri { get; private set; }

    public string? ObservedProviderReference { get; private set; }

    public Uri? ObservedProcessUri { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public void Begin(DateTimeOffset startedAt)
    {
        EnsureState(
            PaymentInitiationState.Prepared,
            PaymentInitiationState.InProgress);

        var normalized = ValidateChangedAt(startedAt);
        State = PaymentInitiationState.InProgress;
        StartedAt = normalized;
        UpdatedAt = normalized;
    }

    public void Complete(
        DateTimeOffset completedAt,
        Uri? processUri = null)
    {
        EnsureState(
            PaymentInitiationState.InProgress,
            PaymentInitiationState.Initialized);

        var normalized = ValidateChangedAt(completedAt);

        var normalizedProcessUri = ValidateProcessUri(
            processUri,
            nameof(processUri));

        State = PaymentInitiationState.Initialized;
        LastError = null;
        ProcessUri = normalizedProcessUri;
        ObservedProviderReference = null;
        ObservedProcessUri = null;
        FinishedAt = normalized;
        UpdatedAt = normalized;
    }

    public void MarkUncertain(
        string reason,
        DateTimeOffset changedAt,
        string? observedProviderReference = null,
        Uri? observedProcessUri = null)
    {
        EnsureState(
            PaymentInitiationState.InProgress,
            PaymentInitiationState.Uncertain);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Důvod nejasného výsledku inicializace nesmí být prázdný.",
                nameof(reason));
        }

        var normalizedReason = reason.Trim();

        if (normalizedReason.Length > MaximumErrorLength)
        {
            normalizedReason = normalizedReason[..MaximumErrorLength];
        }

        var normalizedTime = ValidateChangedAt(changedAt);
        var normalizedProviderReference = string.IsNullOrWhiteSpace(
            observedProviderReference)
            ? null
            : PaymentProviderReference.Normalize(
                observedProviderReference,
                nameof(observedProviderReference));
        var normalizedObservedProcessUri = ValidateProcessUri(
            observedProcessUri,
            nameof(observedProcessUri));

        if (
            normalizedProviderReference is null &&
            normalizedObservedProcessUri is not null)
        {
            throw new ArgumentException(
                "Process URI observation vyžaduje známou provider reference.",
                nameof(observedProcessUri));
        }

        State = PaymentInitiationState.Uncertain;
        LastError = normalizedReason;
        ObservedProviderReference = normalizedProviderReference;
        ObservedProcessUri = normalizedObservedProcessUri;
        FinishedAt = normalizedTime;
        UpdatedAt = normalizedTime;
    }

    public void RecoverObservedInitialization(
        DateTimeOffset recoveredAt)
    {
        EnsureState(
            PaymentInitiationState.Uncertain,
            PaymentInitiationState.Initialized);

        if (ObservedProviderReference is null)
        {
            throw new InvalidOperationException(
                "Nejasná inicializace nemá ověřenou provider reference pro automatickou recovery.");
        }

        var normalizedTime = ValidateChangedAt(recoveredAt);
        State = PaymentInitiationState.Initialized;
        LastError = null;
        ProcessUri = ObservedProcessUri;
        ObservedProviderReference = null;
        ObservedProcessUri = null;
        FinishedAt = normalizedTime;
        UpdatedAt = normalizedTime;
    }

    public void RecordObservedProviderResult(
        string providerReference,
        Uri? processUri,
        DateTimeOffset observedAt)
    {
        if (State != PaymentInitiationState.Uncertain)
        {
            throw new InvalidOperationException(
                "Provider result lze doplnit pouze k nejasné inicializaci.");
        }

        var normalizedReference = PaymentProviderReference.Normalize(
            providerReference,
            nameof(providerReference));
        var normalizedProcessUri = ValidateProcessUri(
            processUri,
            nameof(processUri));

        if (
            ObservedProviderReference is not null &&
            !string.Equals(
                ObservedProviderReference,
                normalizedReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Pozdní provider observation obsahuje konfliktní provider reference.");
        }

        if (
            ObservedProcessUri is not null &&
            normalizedProcessUri is not null &&
            ObservedProcessUri != normalizedProcessUri)
        {
            throw new InvalidOperationException(
                "Pozdní provider observation obsahuje konfliktní process URI.");
        }

        var normalizedTime = ValidateChangedAt(observedAt);
        ObservedProviderReference ??= normalizedReference;
        ObservedProcessUri ??= normalizedProcessUri;
        UpdatedAt = normalizedTime;
    }

    private static Uri? ValidateProcessUri(
        Uri? processUri,
        string parameterName)
    {
        if (processUri is null)
        {
            return null;
        }

        if (
            !processUri.IsAbsoluteUri ||
            processUri.Scheme != Uri.UriSchemeHttps ||
            processUri.AbsoluteUri.Length > MaximumProcessUriLength)
        {
            throw new ArgumentException(
                "Process URI must be an absolute HTTPS URI within the supported length.",
                parameterName);
        }

        return processUri;
    }

    private void EnsureState(
        PaymentInitiationState required,
        PaymentInitiationState target)
    {
        if (State != required)
        {
            throw new InvalidOperationException(
                $"Inicializaci platby nelze změnit ze stavu " +
                $"'{State}' do stavu '{target}'.");
        }
    }

    private DateTimeOffset ValidateChangedAt(
        DateTimeOffset changedAt)
    {
        if (changedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(changedAt),
                "Nový stav inicializace nesmí předcházet poslední změně.");
        }

        return changedAt;
    }
}
