using System.Globalization;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Persistence;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Credits.Infrastructure.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

namespace FuaPay.Web.Modules.Reporting.Application;

internal sealed partial class AccountingReportBuilder(AccountingSnapshot data, Guid? classroomSource)
{
    private readonly List<AccountingProblem> _problems = [];
    private readonly List<AccountingCentreEvent> _centres = [];
    private readonly Dictionary<Guid, CreditAccountEntity> _accounts = data.Accounts.ToDictionary(item => item.Id);
    private readonly Dictionary<Guid, CreditMovementEntity> _movements = data.Movements.ToDictionary(item => item.OperationId);
    private readonly Dictionary<Guid, PaymentEntity> _payments = data.Payments.ToDictionary(item => item.Id);
    private readonly Dictionary<Guid, JobEntity> _jobs = data.Jobs.ToDictionary(item => item.Id);
    private readonly Dictionary<Guid, AccessUserEntity> _users = data.Users.ToDictionary(item => item.Id);
    private readonly Dictionary<(int Type, Guid Id), FinancialDocumentEntity> _documents = data.Documents
        .ToDictionary(item => (item.SourceType, item.SourceId));
    private readonly HashSet<(int, Guid)> _verifiedDocuments = [];
    private int _boundaryEffects;

    public AccountingReport Build()
    {
        var acceptedPayments = ReadPayments();
        var acceptedReturns = ReadReturns(acceptedPayments);
        var movements = ReadMovements(acceptedPayments, acceptedReturns);
        var (balances, ledgerComplete) = ReadBalances();
        var jobs = ReadJobs(acceptedPayments, acceptedReturns);
        var openReturns = data.Returns.Count(item => data.Includes(item.RequestedAt) &&
            item.State is (int)SettlementReturnState.Requested or (int)SettlementReturnState.InProgress or
                (int)SettlementReturnState.RequiresAttention);
        var attention = data.Recoveries.Where(item => item.State == (int)PaymentReconciliationState.RequiresAttention)
            .Select(item => item.PaymentId).ToHashSet();
        var incomplete = data.Payments.Count(item => item.Provider == (int)PaymentProvider.Csob &&
            data.Includes(item.CreatedAt) && (item.Status != (int)PaymentStatus.Succeeded || !data.Includes(item.CompletedAt)));
        foreach (var row in data.Payments.Where(item => attention.Contains(item.Id) &&
            (data.Includes(item.CreatedAt) || data.Includes(item.CompletedAt))).OrderBy(item => item.Id))
        {
            Problem("RECONCILIATION", "Platba vyžaduje ruční kontrolu.", "PaymentRecovery", row.Id, row.AmountMinorUnits);
        }

        return new(data.Period, data.CapturedAt, data.Cutoff, data.RawTotals,
            acceptedPayments.Values.Concat(acceptedReturns.Values)
                .Where(item => data.Includes(item.At)).OrderBy(item => item.At).ThenBy(item => item.Id).ToArray(),
            movements, balances, jobs, _centres.OrderBy(item => item.At).ThenBy(item => item.Id).ToArray(), data.Units,
            _problems.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.Id).ToArray(),
            ledgerComplete, balances.Count(item => item.Opening != 0 && item.LastAt < data.Period.From),
            incomplete, openReturns, _verifiedDocuments.Count, _boundaryEffects);
    }

    private Dictionary<Guid, AccountingCardRow> ReadPayments()
    {
        var result = new Dictionary<Guid, AccountingCardRow>();
        var initiations = data.Initiations.ToDictionary(item => item.PaymentId);
        foreach (var payment in data.Payments.Where(item => item.Provider == (int)PaymentProvider.Csob &&
            item.Status == (int)PaymentStatus.Succeeded))
        {
            var wallet = payment.PurposeType == (int)PaymentPurposeType.CreditTopUp;
            var ownedEffect = wallet
                ? payment.JobId is null && HasMovement(payment.Id, payment.CustomerUserId,
                    CreditMovementType.Credit, payment.AmountMinorUnits)
                : payment.PurposeType == (int)PaymentPurposeType.Job && payment.JobId is { } jobId &&
                    _jobs.TryGetValue(jobId, out var job) && IsDirectJob(job, payment);
            var canonical = IsCanonical(payment.ProviderReference);
            if (payment.CompletedAt is not { } completed || completed < payment.CreatedAt || !ownedEffect ||
                payment.AmountMinorUnits <= 0 || !canonical ||
                !initiations.TryGetValue(payment.Id, out var init) || init.Provider != payment.Provider ||
                init.State != (int)PaymentInitiationState.Initialized || init.OrderNumber <= 0)
            {
                Problem("PAYMENT", "Chybí nebo nesouhlasí kanonická vazba platby, inicializace či zaúčtování.",
                    "Payment", payment.Id, payment.AmountMinorUnits);
                continue;
            }

            var eventAt = wallet ? _movements[payment.Id].RecordedAt : _jobs[payment.JobId!.Value].SettledAt!.Value;
            var document = CheckDocument(FinancialDocumentSourceType.Payment, payment.Id,
                wallet ? FinancialDocumentType.CardWalletTopUp : FinancialDocumentType.DirectJobCardPayment,
                payment.CustomerUserId, payment.AmountMinorUnits, eventAt,
                payment.JobId, payment.ProviderReference, init.OrderNumber.ToString(CultureInfo.InvariantCulture));
            if (document is null || eventAt > completed)
            {
                if (eventAt > completed)
                {
                    Problem("PAYMENT_TIME", "Zaúčtování následuje až po dokončení platby.", "Payment", payment.Id);
                }

                continue;
            }

            if (data.Includes(completed) != data.Includes(eventAt))
            {
                _boundaryEffects++;
            }

            result.Add(payment.Id, new(payment.Id, payment.Id, completed,
                wallet ? "Dobití kreditu" : "Platba zakázky", document.DocumentNumber, document.DocumentId,
                payment.ProviderReference!, init.OrderNumber, payment.AmountMinorUnits, 0, wallet));
        }

        return result;
    }

    private Dictionary<Guid, AccountingCardRow> ReadReturns(Dictionary<Guid, AccountingCardRow> acceptedPayments)
    {
        var result = new Dictionary<Guid, AccountingCardRow>();
        var attempts = data.Attempts.ToLookup(item => item.SettlementReturnId);
        var groups = data.Returns.Where(item => item.State == (int)SettlementReturnState.Completed &&
            item.Kind is (int)SettlementReturnKind.CardTopUp or (int)SettlementReturnKind.CardJob)
            .Where(item => item.CompletedAt is null || item.CompletedAt.Value < data.Cutoff)
            .GroupBy(item => item.OriginalPaymentId);
        foreach (var group in groups)
        {
            var withinOriginal = group.Key is { } paymentId && acceptedPayments.TryGetValue(paymentId, out var original) &&
                group.All(row => row.AmountMinorUnits > 0) &&
                group.Sum(item => item.AmountMinorUnits) <= original.Received;
            foreach (var row in group)
            {
                if (!withinOriginal || row.OriginalPaymentId is not { } id ||
                    !acceptedPayments.TryGetValue(id, out var payment) ||
                    !IsCardReturn(row, _payments[id], payment, attempts[row.Id], out var operation))
                {
                    Problem("RETURN", "Dokončená vratka nemá jednoznačnou původní platbu, pokus nebo finanční účinek.",
                        "SettlementReturn", row.Id, row.AmountMinorUnits);
                    continue;
                }

                result.Add(row.Id, payment with
                {
                    Id = row.Id,
                    At = row.CompletedAt!.Value,
                    Operation = operation,
                    Received = 0,
                    Returned = row.AmountMinorUnits
                });
            }
        }

        return result;
    }

    private bool IsCardReturn(SettlementReturnEntity row, PaymentEntity original, AccountingCardRow payment,
        IEnumerable<SettlementReturnProviderAttemptEntity> attempts, out string operation)
    {
        operation = string.Empty;
        if (row.CompletedAt is not { } completed || completed < payment.At || row.AmountMinorUnits <= 0 ||
            row.Currency != Money.CurrencyCode || row.CustomerUserId != original.CustomerUserId ||
            row.Kind != (int)(payment.Wallet ? SettlementReturnKind.CardTopUp : SettlementReturnKind.CardJob) ||
            row.JobId != original.JobId ||
            (payment.Wallet && (row.AmountMinorUnits != payment.Received ||
                !HasMovement(row.Id, row.CustomerUserId, CreditMovementType.Debit, row.AmountMinorUnits) ||
                _movements[row.Id].RecordedAt != completed)))
        {
            return false;
        }

        var confirmed = attempts.Where(item => item.State == (int)SettlementReturnProviderAttemptState.Confirmed)
            .ToArray();
        if (confirmed.Length != 1 || confirmed[0].Provider != (int)PaymentProvider.Csob ||
            !string.Equals(confirmed[0].ProviderReference, payment.PayId, StringComparison.Ordinal) ||
            confirmed[0].StartedAt is not { } started || started > completed ||
            confirmed[0].FinishedAt is not { } finished || finished < completed)
        {
            return false;
        }

        operation = confirmed[0].Operation switch
        {
            (int)SettlementReturnProviderOperation.Reverse when row.AmountMinorUnits == payment.Received => "Storno karty",
            (int)SettlementReturnProviderOperation.Refund => "Vratka kartou",
            _ => string.Empty
        };
        return operation.Length > 0;
    }

    private FinancialDocumentEntity? CheckDocument(FinancialDocumentSourceType source, Guid id,
        FinancialDocumentType type, Guid owner, long amount, DateTimeOffset at, Guid? jobId,
        string? payId = null, string? orderNumber = null, bool legacyAllowed = false)
    {
        if (!_documents.TryGetValue(((int)source, id), out var doc))
        {
            if (!legacyAllowed)
            {
                Problem("DOCUMENT_MISSING", "Chybí kanonický doklad a není doložená historická výjimka.", "FinancialDocument", id, amount);
            }

            return null;
        }

        if (doc.DocumentType != (int)type || doc.CustomerUserId != owner || doc.AmountMinorUnits != amount ||
            doc.Currency != Money.CurrencyCode || doc.FinancialEventAt != at || doc.JobId != jobId ||
            string.IsNullOrWhiteSpace(doc.DocumentNumber) ||
            (payId is not null && (doc.Provider != "Csob" || doc.ProviderReference != payId ||
                doc.ProviderOrderNumber != orderNumber)))
        {
            Problem("DOCUMENT_CONFLICT", "Snapshot dokladu nesouhlasí s finančním zdrojem.", "FinancialDocument", id, amount);
            return null;
        }

        try
        {
            _ = EfFinancialDocumentRepository.Restore(doc);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or OverflowException)
        {
            Problem("DOCUMENT_SNAPSHOT", "Snapshot dokladu nelze ověřit.", "FinancialDocument", id, amount);
            return null;
        }

        if (doc.SchemaVersion == 1)
        {
            Problem("DOCUMENT_LEGACY", "Historický doklad neobsahuje úplný snapshot DPH.", "FinancialDocument", id);
        }
        else if (data.Includes(at) || data.Includes(doc.IssuedAt))
        {
            _verifiedDocuments.Add(((int)source, id));
        }

        return doc;
    }

    private static bool IsDirectJob(JobEntity job, PaymentEntity payment) =>
        job.JobPaidAs(JobSettlementType.DirectPayment) && job.SettlementReferenceId == payment.Id &&
        job.CustomerUserId == payment.CustomerUserId && job.PriceMinorUnits == payment.AmountMinorUnits;

    private static bool IsCanonical(string? value)
    {
        if (value is null)
        {
            return false;
        }

        try
        {
            _ = CsobPayId.RequireCanonical(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool HasMovement(Guid op, Guid owner, CreditMovementType type, long amount) =>
        _movements.TryGetValue(op, out var item) && item.MovementType == (int)type &&
        item.AmountMinorUnits == amount && _accounts.TryGetValue(item.AccountId, out var account) &&
        account.OwnerId == owner;

    private void Problem(string code, string reason, string source, Guid? id = null, long? amount = null) =>
        _problems.Add(new(code, reason, source, id, amount));

    private AccountingParty Party(Guid owner) => _users.TryGetValue(owner, out var user)
        ? new(user.DisplayName, user.Email) : new("Neznámý uživatel", null);
}

internal static class AccountingJobSettlement
{
    public static bool JobPaidAs(this JobEntity job, JobSettlementType type) =>
        job.PaymentStatus == (int)JobPaymentStatus.Paid && job.SettledAt.HasValue && job.SettlementType == (int)type;
}
