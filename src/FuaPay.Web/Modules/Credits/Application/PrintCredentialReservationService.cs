using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class PrintCredentialReservationService
{
    private readonly IPrintCredentialRepository _credentials;
    private readonly IPrintCodeHasher _hasher;
    private readonly IPrintCredentialAttemptLimiter _attemptLimiter;
    private readonly PrintReservationService _reservations;
    private readonly TimeProvider _timeProvider;
    private readonly string _unknownCredentialHash;

    public PrintCredentialReservationService(
        IPrintCredentialRepository credentials,
        IPrintCodeHasher hasher,
        IPrintCredentialAttemptLimiter attemptLimiter,
        PrintReservationService reservations,
        TimeProvider timeProvider)
    {
        _credentials = credentials;
        _hasher = hasher;
        _attemptLimiter = attemptLimiter;
        _reservations = reservations;
        _timeProvider = timeProvider;
        _unknownCredentialHash = hasher.Hash("000000");
    }

    public async Task<PrintReservationResult> ReserveAsync(
        Guid printSourceId,
        string sourceAddress,
        string email,
        string printCode,
        string jobUuid,
        Money amount,
        Guid reserveCommandId,
        CancellationToken cancellationToken = default)
    {
        if (
            !PrintCredentialEmail.TryNormalize(email, out var normalizedEmail) ||
            !PrintCodePolicy.IsValid(printCode))
        {
            throw new PrintCredentialAuthenticationFailedException();
        }

        if (!_attemptLimiter.TryAcquire(
                printSourceId,
                sourceAddress,
                normalizedEmail,
                _timeProvider.GetUtcNow()))
        {
            throw new PrintCredentialRateLimitExceededException();
        }

        return await _reservations.ReserveAuthenticatedAsync(
            async ct =>
            {
                var candidate = await _credentials
                    .FindAuthenticationCandidateAsync(
                        normalizedEmail,
                        ct);
                var hash = candidate?.CodeHash ?? _unknownCredentialHash;
                var verified = _hasher.Verify(hash, printCode);

                if (!verified || candidate?.IsEligible != true)
                {
                    throw new PrintCredentialAuthenticationFailedException();
                }

                return new ReservePrintCreditCommand(
                    candidate.OwnerId,
                    printSourceId,
                    jobUuid,
                    amount,
                    reserveCommandId);
            },
            cancellationToken);
    }
}
