using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class LegacySafeQCreditTransferService
{
    public const string CustomerDescription = "Převod kreditu ze SafeQ";

    private readonly CreditService _creditService;
    private readonly ILegacySafeQCreditTransferRepository _repository;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly TimeProvider _timeProvider;

    public LegacySafeQCreditTransferService(
        CreditService creditService,
        ILegacySafeQCreditTransferRepository repository,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        IAccessUserQueries accessUserQueries,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(creditService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _creditService = creditService;
        _repository = repository;
        _transaction = transaction;
        _auditTrail = auditTrail;
        _accessUserQueries = accessUserQueries;
        _timeProvider = timeProvider;
    }

    public async Task<LegacySafeQCreditTransferResult> TransferAsync(
        LegacySafeQCreditTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _repository.FindByCommandIdAsync(
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

            var persisted = await _repository.FindByCommandIdAsync(
                command.CommandId,
                cancellationToken);

            if (persisted is null)
            {
                throw new InvalidDataException(
                    $"Persisted SafeQ credit transfer '{command.CommandId}' " +
                    "was not found after a successful transaction.");
            }

            return ResolveReplay(command, persisted);
        }
        catch (Exception exception) when (
            exception is
                LegacySafeQCreditTransferCommandAlreadyExistsException or
                LegacySafeQCreditAlreadyTransferredException or
                DuplicateCreditOperationException or
                CreditAccountConcurrencyException)
        {
            var concurrentCommand = await _repository.FindByCommandIdAsync(
                command.CommandId,
                cancellationToken);

            if (concurrentCommand is not null)
            {
                return ResolveReplay(command, concurrentCommand);
            }

            var concurrentSource = await _repository.FindBySafeQUserIdAsync(
                command.SafeQUserId,
                cancellationToken);

            if (concurrentSource is not null)
            {
                throw new LegacySafeQCreditAlreadyTransferredException(
                    command.SafeQUserId,
                    exception);
            }

            throw;
        }
    }

    private async Task<LegacySafeQCreditTransferResult>
        ApplyInsideTransactionAsync(
            LegacySafeQCreditTransferCommand command,
            CancellationToken cancellationToken)
    {
        var existingCommand = await _repository.FindByCommandIdAsync(
            command.CommandId,
            cancellationToken);

        if (existingCommand is not null)
        {
            return ResolveReplay(command, existingCommand);
        }

        var existingSource = await _repository.FindBySafeQUserIdAsync(
            command.SafeQUserId,
            cancellationToken);

        if (existingSource is not null)
        {
            throw new LegacySafeQCreditAlreadyTransferredException(
                command.SafeQUserId);
        }

        var ownerIsEligible =
            await _accessUserQueries.IsActiveCustomerAsync(
                command.OwnerId,
                cancellationToken);

        if (!ownerIsEligible)
        {
            throw new LegacySafeQCreditTransferOwnerNotEligibleException(
                command.OwnerId);
        }

        var acceptedAt = _timeProvider.GetUtcNow();

        _repository.Stage(command, acceptedAt);

        _auditTrail.Stage(AuditEntry.ForUser(
            command.AdministratorUserId,
            "credit.legacy-safeq-transfer",
            "credit-account",
            command.OwnerId.ToString(),
            $"SafeQ účet {command.SafeQUserId} ze snapshotu " +
            $"{command.SnapshotSha256} byl převeden uživateli " +
            $"{command.OwnerId} částkou {command.Amount.MinorUnits} haléřů " +
            $"příkazem {command.CommandId}.",
            acceptedAt));

        var movement = await _creditService.CreditAsync(
            command.OwnerId,
            command.CommandId,
            command.Amount,
            CustomerDescription,
            cancellationToken);

        return ToResult(command.CommandId, movement);
    }

    private static LegacySafeQCreditTransferResult ResolveReplay(
        LegacySafeQCreditTransferCommand attempted,
        PersistedLegacySafeQCreditTransfer persisted)
    {
        if (
            attempted.AdministratorUserId !=
                persisted.Command.AdministratorUserId ||
            attempted.OwnerId != persisted.Command.OwnerId ||
            attempted.Amount != persisted.Command.Amount ||
            !string.Equals(
                attempted.SafeQUserId,
                persisted.Command.SafeQUserId,
                StringComparison.Ordinal) ||
            !string.Equals(
                attempted.SnapshotSha256,
                persisted.Command.SnapshotSha256,
                StringComparison.Ordinal))
        {
            throw new LegacySafeQCreditTransferCommandConflictException(
                attempted.CommandId);
        }

        return persisted.Result;
    }

    private static LegacySafeQCreditTransferResult ToResult(
        Guid commandId,
        CreditMovement movement)
    {
        return new LegacySafeQCreditTransferResult(
            commandId,
            movement.Type,
            movement.Amount,
            movement.BalanceAfter,
            movement.RecordedAt,
            movement.Description);
    }
}
