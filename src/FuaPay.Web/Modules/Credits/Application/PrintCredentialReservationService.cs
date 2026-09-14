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
        IPreparedPrintCredentialHash preparedDummyHash,
        IPrintCredentialAttemptLimiter attemptLimiter,
        PrintReservationService reservations,
        TimeProvider timeProvider)
    {
        _credentials = credentials;
        _hasher = hasher;
        _attemptLimiter = attemptLimiter;
        _reservations = reservations;
        _timeProvider = timeProvider;
        _unknownCredentialHash = preparedDummyHash.Value;
    }

    public async Task<PrintReservationResult> ReserveAsync(
        Guid printSourceId,
        string email,
        string printCode,
        string jobUuid,
        Money amount,
        Guid reserveCommandId,
        CancellationToken cancellationToken = default)
    {
        var hasNormalizedEmail = PrintCredentialEmail.TryNormalize(
            email,
            out var normalizedEmail);
        var attemptedEmail = hasNormalizedEmail
            ? normalizedEmail
            : null;
        var now = _timeProvider.GetUtcNow();

        if (_attemptLimiter.IsBlocked(
                printSourceId,
                attemptedEmail,
                now))
        {
            throw new PrintCredentialRateLimitExceededException();
        }

        if (!hasNormalizedEmail || !PrintCodePolicy.IsValid(printCode))
        {
            throw AuthenticationFailureAfterRecording(
                printSourceId,
                attemptedEmail);
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
                    throw AuthenticationFailureAfterRecording(
                        printSourceId,
                        normalizedEmail);
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

    private Exception AuthenticationFailureAfterRecording(
        Guid printSourceId,
        string? normalizedEmail)
    {
        if (!_attemptLimiter.TryRecordFailure(
                printSourceId,
                normalizedEmail,
                _timeProvider.GetUtcNow()))
        {
            throw new PrintCredentialRateLimitExceededException();
        }

        return new PrintCredentialAuthenticationFailedException();
    }
}
