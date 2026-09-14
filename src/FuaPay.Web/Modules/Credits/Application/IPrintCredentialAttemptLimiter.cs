namespace FuaPay.Web.Modules.Credits.Application;

public interface IPrintCredentialAttemptLimiter
{
    bool TryAcquire(
        Guid printSourceId,
        string sourceAddress,
        string normalizedEmail,
        DateTimeOffset now);
}
