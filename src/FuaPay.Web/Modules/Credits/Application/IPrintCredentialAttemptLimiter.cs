namespace FuaPay.Web.Modules.Credits.Application;

public interface IPrintCredentialAttemptLimiter
{
    bool IsBlocked(
        Guid printSourceId,
        string? normalizedEmail,
        DateTimeOffset now);

    bool TryRecordFailure(
        Guid printSourceId,
        string? normalizedEmail,
        DateTimeOffset now);
}
