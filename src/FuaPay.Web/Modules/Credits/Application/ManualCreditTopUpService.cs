using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class ManualCreditTopUpService
{
    private readonly CreditService _creditService;
    private readonly IManualCreditTopUpCommandRepository _commandRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;

    public ManualCreditTopUpService(
        CreditService creditService,
        IManualCreditTopUpCommandRepository commandRepository,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(creditService);
        ArgumentNullException.ThrowIfNull(commandRepository);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _creditService = creditService;
        _commandRepository = commandRepository;
        _transaction = transaction;
        _auditTrail = auditTrail;
        _timeProvider = timeProvider;
    }

    public async Task<ManualCreditTopUpResult> TopUpAsync(
        ManualCreditTopUpCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _commandRepository.FindAsync(
            command.CommandId,
            cancellationToken);

        if (existing is not null)
        {
            return ResolveReplay(command, existing);
        }

        try
        {
            await _transaction.ExecuteAsync(
                ct => ApplyInsideTransactionAsync(command, ct),
                cancellationToken);

            var persisted = await _commandRepository.FindAsync(
                command.CommandId,
                cancellationToken);

            if (persisted is null)
            {
                throw new InvalidDataException(
                    $"Persisted manual credit top-up command '{command.CommandId}' " +
                    "was not found after a successful transaction.");
            }

            return ResolveReplay(command, persisted);
        }
        catch (Exception exception) when (
            exception is
                ManualCreditTopUpCommandAlreadyExistsException or
                DuplicateCreditOperationException or
                CreditAccountConcurrencyException)
        {
            var concurrent = await _commandRepository.FindAsync(
                command.CommandId,
                cancellationToken);

            if (concurrent is not null)
            {
                return ResolveReplay(command, concurrent);
            }

            throw;
        }
    }

    private async Task<ManualCreditTopUpResult> ApplyInsideTransactionAsync(
        ManualCreditTopUpCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _commandRepository.FindAsync(
            command.CommandId,
            cancellationToken);

        if (existing is not null)
        {
            return ResolveReplay(command, existing);
        }

        var acceptedAt = _timeProvider.GetUtcNow();
        var description =
            $"Ruční dobití kreditu: {command.Note} " +
            $"(provedl {command.AdministratorUserId}, příkaz {command.CommandId})";

        _commandRepository.Stage(command, acceptedAt);

        _auditTrail.Stage(AuditEntry.ForUser(
            command.AdministratorUserId,
            "credit.manual-topup",
            "credit-account",
            command.OwnerId.ToString(),
            $"Kredit uživatele {command.OwnerId} byl ručně dobit o " +
            $"{command.Amount.MinorUnits} haléřů příkazem {command.CommandId}. " +
            $"Poznámka: {command.Note}",
            acceptedAt));

        var movement = await _creditService.CreditAsync(
            command.OwnerId,
            command.CommandId,
            command.Amount,
            description,
            cancellationToken);

        return ToResult(command.CommandId, movement);
    }

    private static ManualCreditTopUpResult ResolveReplay(
        ManualCreditTopUpCommand attempted,
        PersistedManualCreditTopUpCommand persisted)
    {
        if (
            attempted.AdministratorUserId !=
                persisted.Command.AdministratorUserId ||
            attempted.OwnerId != persisted.Command.OwnerId ||
            attempted.Amount != persisted.Command.Amount ||
            !string.Equals(
                attempted.Note,
                persisted.Command.Note,
                StringComparison.Ordinal))
        {
            throw new ManualCreditTopUpCommandConflictException(
                attempted.CommandId);
        }

        return persisted.Result;
    }

    private static ManualCreditTopUpResult ToResult(
        Guid commandId,
        CreditMovement movement)
    {
        return new ManualCreditTopUpResult(
            commandId,
            movement.Type,
            movement.Amount,
            movement.BalanceAfter,
            movement.RecordedAt,
            movement.Description);
    }
}
