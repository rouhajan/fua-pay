using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class CreditAdministrationService
{
    private readonly CreditService _creditService;
    private readonly ICreditAdjustmentCommandRepository _commandRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;

    public CreditAdministrationService(
        CreditService creditService,
        ICreditAdjustmentCommandRepository commandRepository,
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

    public async Task<CreditAdjustmentResult> AdjustAsync(
        CreditAdjustmentCommand command,
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
                    $"Persisted credit adjustment command '{command.CommandId}' " +
                    "was not found after a successful transaction.");
            }

            return ResolveReplay(command, persisted);
        }
        catch (Exception exception) when (
            exception is
                CreditAdjustmentCommandAlreadyExistsException or
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

    private async Task<CreditAdjustmentResult> ApplyInsideTransactionAsync(
        CreditAdjustmentCommand command,
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
            $"Administrativní korekce: {command.Reason} " +
            $"(provedl {command.AdministratorUserId}, příkaz {command.CommandId})";

        _commandRepository.Stage(command, acceptedAt);

        _auditTrail.Stage(AuditEntry.ForUser(
            command.AdministratorUserId,
            "credit.adjusted",
            "credit-account",
            command.OwnerId.ToString(),
            $"Kredit uživatele {command.OwnerId} byl upraven o {command.SignedAmount.MinorUnits} haléřů příkazem {command.CommandId}. Důvod: {command.Reason}",
            acceptedAt));

        CreditMovement movement;

        if (command.SignedAmount.MinorUnits > 0)
        {
            movement = await _creditService.CreditAsync(
                command.OwnerId,
                command.CommandId,
                command.SignedAmount,
                description,
                cancellationToken);
        }
        else
        {
            movement = await _creditService.DebitAsync(
                command.OwnerId,
                command.CommandId,
                command.SignedAmount.Negate(),
                description,
                cancellationToken);
        }

        return ToResult(command.CommandId, movement);
    }

    private static CreditAdjustmentResult ResolveReplay(
        CreditAdjustmentCommand attempted,
        PersistedCreditAdjustmentCommand persisted)
    {
        if (
            attempted.AdministratorUserId !=
                persisted.Command.AdministratorUserId ||
            attempted.OwnerId != persisted.Command.OwnerId ||
            attempted.SignedAmount != persisted.Command.SignedAmount ||
            !string.Equals(
                attempted.Reason,
                persisted.Command.Reason,
                StringComparison.Ordinal))
        {
            throw new CreditAdjustmentCommandConflictException(
                attempted.CommandId);
        }

        return persisted.Result;
    }

    private static CreditAdjustmentResult ToResult(
        Guid commandId,
        CreditMovement movement)
    {
        return new CreditAdjustmentResult(
            commandId,
            movement.Type,
            movement.Amount,
            movement.BalanceAfter,
            movement.RecordedAt,
            movement.Description);
    }
}
