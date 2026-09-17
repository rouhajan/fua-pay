using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class ManualCreditTopUpService
{
    private readonly CreditService _creditService;
    private readonly IManualCreditTopUpCommandRepository _commandRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly IFinancialDocumentRepository _financialDocuments;
    private readonly IFinancialDocumentNumberAllocator _documentNumbers;
    private readonly IFinancialDocumentIssuanceProfile _issuanceProfile;
    private readonly TimeProvider _timeProvider;

    public ManualCreditTopUpService(
        CreditService creditService,
        IManualCreditTopUpCommandRepository commandRepository,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        IFinancialDocumentRepository financialDocuments,
        IFinancialDocumentNumberAllocator documentNumbers,
        IFinancialDocumentIssuanceProfile issuanceProfile,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(creditService);
        ArgumentNullException.ThrowIfNull(commandRepository);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(financialDocuments);
        ArgumentNullException.ThrowIfNull(documentNumbers);
        ArgumentNullException.ThrowIfNull(issuanceProfile);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _creditService = creditService;
        _commandRepository = commandRepository;
        _transaction = transaction;
        _auditTrail = auditTrail;
        _financialDocuments = financialDocuments;
        _documentNumbers = documentNumbers;
        _issuanceProfile = issuanceProfile;
        _timeProvider = timeProvider;
    }

    public async Task<ManualCreditTopUpResult> TopUpAsync(
        ManualCreditTopUpCommand command,
        FinancialDocumentCustomerSnapshot customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(customer);

        if (customer.CustomerUserId != command.OwnerId)
        {
            throw new ArgumentException(
                "Customer snapshot must belong to the manual top-up owner.",
                nameof(customer));
        }

        try
        {
            await _transaction.ExecuteTopLevelAsync(
                ct => ApplyInsideTransactionAsync(command, customer, ct),
                cancellationToken);

            return await ResolvePersistedReplayAsync(
                command,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is
                ManualCreditTopUpCommandAlreadyExistsException or
                DuplicateCreditOperationException or
                CreditAccountConcurrencyException or
                FinancialDocumentSourceAlreadyExistsException)
        {
            var concurrent = await _commandRepository.FindAsync(
                command.CommandId,
                cancellationToken);

            if (concurrent is not null)
            {
                return await ResolveReplayAsync(
                    command,
                    concurrent,
                    cancellationToken);
            }

            throw;
        }
    }

    private async Task<ManualCreditTopUpResult> ApplyInsideTransactionAsync(
        ManualCreditTopUpCommand command,
        FinancialDocumentCustomerSnapshot customer,
        CancellationToken cancellationToken)
    {
        var existing = await _commandRepository.FindAsync(
            command.CommandId,
            cancellationToken);

        if (existing is not null)
        {
            return await ResolveReplayAsync(
                command,
                existing,
                cancellationToken);
        }

        var acceptedAt = _timeProvider.GetUtcNow();
        const string description = "Ruční dobití kreditu";

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

        var issuedAt = _timeProvider.GetUtcNow();

        if (issuedAt < movement.RecordedAt)
        {
            issuedAt = movement.RecordedAt;
        }

        var allocation = await _documentNumbers.AllocateAsync(
            issuedAt,
            cancellationToken);

        var document = FinancialDocument.CreateManualCreditTopUp(
            Guid.NewGuid(),
            allocation.DocumentNumber,
            command.CommandId,
            customer,
            command.Amount.MinorUnits,
            Money.CurrencyCode,
            movement.RecordedAt,
            issuedAt,
            _issuanceProfile.CreateIssuerSnapshot(),
            _issuanceProfile.CreateTaxSnapshot(
                command.Amount.MinorUnits));

        _financialDocuments.Stage(document);
        await _financialDocuments.PersistStagedAsync(
            document,
            cancellationToken);

        return ToResult(command.CommandId, movement);
    }

    private async Task<ManualCreditTopUpResult> ResolvePersistedReplayAsync(
        ManualCreditTopUpCommand attempted,
        CancellationToken cancellationToken)
    {
        var persisted = await _commandRepository.FindAsync(
            attempted.CommandId,
            cancellationToken);

        if (persisted is null)
        {
            throw new InvalidDataException(
                $"Persisted manual credit top-up command '{attempted.CommandId}' was not found.");
        }

        return await ResolveReplayAsync(
            attempted,
            persisted,
            cancellationToken);
    }

    private async Task<ManualCreditTopUpResult> ResolveReplayAsync(
        ManualCreditTopUpCommand attempted,
        PersistedManualCreditTopUpCommand persisted,
        CancellationToken cancellationToken)
    {
        var result = ResolveReplay(attempted, persisted);

        if (!persisted.FinancialDocumentRequired)
        {
            return result;
        }

        var document = await _financialDocuments.FindBySourceAsync(
            FinancialDocumentSourceType.ManualCreditTopUp,
            attempted.CommandId,
            cancellationToken);

        if (
            document is null ||
            document.DocumentType != FinancialDocumentType.ManualCreditTopUp ||
            document.Customer.CustomerUserId != persisted.Command.OwnerId ||
            document.AmountMinorUnits != persisted.Command.Amount.MinorUnits ||
            !string.Equals(
                document.Currency,
                Money.CurrencyCode,
                StringComparison.Ordinal) ||
            document.FinancialEventAt != result.RecordedAt)
        {
            throw new InvalidDataException(
                $"Manual credit top-up '{attempted.CommandId}' does not have a consistent financial document.");
        }

        return result;
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
