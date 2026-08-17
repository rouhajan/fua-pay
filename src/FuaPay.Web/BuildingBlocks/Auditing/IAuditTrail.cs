namespace FuaPay.Web.BuildingBlocks.Auditing;

public interface IAuditTrail
{
    void Stage(AuditEntry entry);

    Task WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default);
}

public sealed class NullAuditTrail : IAuditTrail
{
    public static NullAuditTrail Instance { get; } = new();

    private NullAuditTrail()
    {
    }

    public void Stage(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
    }

    public Task WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
