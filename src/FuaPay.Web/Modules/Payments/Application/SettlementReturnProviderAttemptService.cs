using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record CreateSettlementReturnProviderAttemptCommand(
    Guid AttemptId,
    Guid SettlementReturnId,
    SettlementReturnProviderOperation Operation);

public sealed record SettlementReturnProviderAttemptCreationResult(
    SettlementReturnProviderAttempt Attempt,
    bool Created);

public sealed class SettlementReturnProviderAttemptService
{
    private readonly ISettlementReturnProviderAttemptRepository
        _attemptRepository;
    private readonly ISettlementReturnRepository _returnRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly TimeProvider _timeProvider;

    public SettlementReturnProviderAttemptService(
        ISettlementReturnProviderAttemptRepository attemptRepository,
        ISettlementReturnRepository returnRepository,
        IPaymentRepository paymentRepository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(attemptRepository);
        ArgumentNullException.ThrowIfNull(returnRepository);
        ArgumentNullException.ThrowIfNull(paymentRepository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _attemptRepository = attemptRepository;
        _returnRepository = returnRepository;
        _paymentRepository = paymentRepository;
        _timeProvider = timeProvider;
    }

    public async Task<SettlementReturnProviderAttemptCreationResult>
        CreateAsync(
            CreateSettlementReturnProviderAttemptCommand command,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateId(command.AttemptId, nameof(command.AttemptId));
        ValidateId(
            command.SettlementReturnId,
            nameof(command.SettlementReturnId));
        ValidateOperation(command.Operation);

        var existing = await _attemptRepository.FindByIdAsync(
            command.AttemptId,
            cancellationToken);

        if (existing is not null)
        {
            return ResolveReplay(command, existing);
        }

        var settlementReturn = await _returnRepository.FindByIdAsync(
            command.SettlementReturnId,
            cancellationToken)
            ?? throw new SettlementReturnProviderAttemptNotAllowedException(
                command.SettlementReturnId,
                "the settlement return does not exist");

        EnsureProviderAttemptAllowed(settlementReturn);

        var active =
            await _attemptRepository.FindActiveBySettlementReturnIdAsync(
                command.SettlementReturnId,
                cancellationToken);

        if (active is not null)
        {
            throw new SettlementReturnProviderAttemptAlreadyActiveException(
                command.SettlementReturnId,
                active.Id);
        }

        var history = await _attemptRepository.ListBySettlementReturnIdAsync(
            command.SettlementReturnId,
            cancellationToken);

        if (history.Any(item =>
                item.State ==
                    SettlementReturnProviderAttemptState.Confirmed))
        {
            throw new SettlementReturnProviderAttemptNotAllowedException(
                command.SettlementReturnId,
                "a provider attempt has already been confirmed");
        }

        var originalPaymentId = settlementReturn.OriginalPaymentId!.Value;
        var payment = await _paymentRepository.FindByIdAsync(
            originalPaymentId,
            cancellationToken)
            ?? throw new SettlementReturnProviderAttemptNotAllowedException(
                settlementReturn.Id,
                "the authoritative original payment does not exist");

        if (
            payment.Status != PaymentStatus.Succeeded ||
            payment.ProviderReference is null)
        {
            throw new SettlementReturnProviderAttemptNotAllowedException(
                settlementReturn.Id,
                "the authoritative original payment is not successfully " +
                "settled with a provider reference");
        }

        var paymentMatchesReturn =
            payment.CustomerUserId == settlementReturn.CustomerUserId &&
            payment.Amount == settlementReturn.Amount &&
            settlementReturn.Kind switch
            {
                SettlementReturnKind.CardJob =>
                    payment.PurposeType == PaymentPurposeType.Job &&
                    payment.JobId == settlementReturn.JobId,
                SettlementReturnKind.CardTopUp =>
                    payment.PurposeType == PaymentPurposeType.CreditTopUp &&
                    payment.JobId is null,
                _ => false
            };

        if (!paymentMatchesReturn)
        {
            throw new SettlementReturnProviderAttemptNotAllowedException(
                settlementReturn.Id,
                "the authoritative original payment does not match the " +
                "settlement return");
        }

        var candidate = new SettlementReturnProviderAttempt(
            command.AttemptId,
            settlementReturn.Id,
            payment.Provider,
            command.Operation,
            payment.ProviderReference,
            _timeProvider.GetUtcNow());

        try
        {
            await _attemptRepository.AddAsync(
                candidate,
                cancellationToken);

            return new SettlementReturnProviderAttemptCreationResult(
                candidate,
                Created: true);
        }
        catch (SettlementReturnProviderAttemptAlreadyExistsException)
        {
            var concurrent = await _attemptRepository.FindByIdAsync(
                command.AttemptId,
                cancellationToken);

            if (concurrent is null)
            {
                throw;
            }

            return ResolveReplay(command, concurrent);
        }
        catch (SettlementReturnProviderAttemptAlreadyActiveException exception)
        {
            var concurrent =
                await _attemptRepository.FindActiveBySettlementReturnIdAsync(
                    command.SettlementReturnId,
                    cancellationToken);

            if (concurrent?.Id == command.AttemptId)
            {
                return ResolveReplay(command, concurrent);
            }

            throw new SettlementReturnProviderAttemptAlreadyActiveException(
                command.SettlementReturnId,
                concurrent?.Id,
                exception);
        }
    }

    public Task<SettlementReturnProviderAttempt> BeginAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        return ChangeAsync(
            attemptId,
            attempt => attempt.Begin(_timeProvider.GetUtcNow()),
            cancellationToken);
    }

    public Task<SettlementReturnProviderAttempt> ConfirmAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        return ChangeAsync(
            attemptId,
            attempt => attempt.Confirm(_timeProvider.GetUtcNow()),
            cancellationToken);
    }

    public Task<SettlementReturnProviderAttempt> RejectAsync(
        Guid attemptId,
        string diagnostic,
        CancellationToken cancellationToken = default)
    {
        return ChangeAsync(
            attemptId,
            attempt => attempt.Reject(
                diagnostic,
                _timeProvider.GetUtcNow()),
            cancellationToken);
    }

    public Task<SettlementReturnProviderAttempt> MarkUncertainAsync(
        Guid attemptId,
        string diagnostic,
        CancellationToken cancellationToken = default)
    {
        return ChangeAsync(
            attemptId,
            attempt => attempt.MarkUncertain(
                diagnostic,
                _timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private async Task<SettlementReturnProviderAttempt> ChangeAsync(
        Guid attemptId,
        Action<SettlementReturnProviderAttempt> change,
        CancellationToken cancellationToken)
    {
        ValidateId(attemptId, nameof(attemptId));
        ArgumentNullException.ThrowIfNull(change);

        var attempt = await _attemptRepository.FindByIdAsync(
            attemptId,
            cancellationToken)
            ?? throw new SettlementReturnProviderAttemptNotFoundException(
                attemptId);

        change(attempt);
        await _attemptRepository.SaveAsync(attempt, cancellationToken);
        return attempt;
    }

    private static SettlementReturnProviderAttemptCreationResult
        ResolveReplay(
            CreateSettlementReturnProviderAttemptCommand command,
            SettlementReturnProviderAttempt existing)
    {
        if (
            existing.Id != command.AttemptId ||
            existing.SettlementReturnId != command.SettlementReturnId ||
            existing.Operation != command.Operation)
        {
            throw new SettlementReturnProviderAttemptConflictException(
                command.AttemptId);
        }

        return new SettlementReturnProviderAttemptCreationResult(
            existing,
            Created: false);
    }

    private static void EnsureProviderAttemptAllowed(
        SettlementReturn settlementReturn)
    {
        if (
            settlementReturn.Kind == SettlementReturnKind.CreditJob ||
            !settlementReturn.OriginalPaymentId.HasValue)
        {
            throw new SettlementReturnProviderAttemptNotAllowedException(
                settlementReturn.Id,
                "a credit-paid job has no external payment provider");
        }

        if (
            settlementReturn.State is SettlementReturnState.Completed or
                SettlementReturnState.Rejected)
        {
            throw new SettlementReturnProviderAttemptNotAllowedException(
                settlementReturn.Id,
                "the settlement return is already terminal");
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
                "Provider attempt command ID must not be empty.",
                parameterName);
        }
    }
}
