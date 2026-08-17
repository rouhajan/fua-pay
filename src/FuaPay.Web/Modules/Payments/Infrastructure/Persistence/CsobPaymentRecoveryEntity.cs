namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class CsobPaymentRecoveryEntity
{
    public Guid PaymentId { get; set; }

    public string? ProviderReference { get; set; }

    public int State { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public Guid? LeaseToken { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? LastBrowserReturnAt { get; set; }

    public int? LastGatewayPaymentStatus { get; set; }

    public int? LastResultCode { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public long Version { get; set; }
}
