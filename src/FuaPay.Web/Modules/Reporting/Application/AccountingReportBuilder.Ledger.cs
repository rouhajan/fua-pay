using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Credits.Infrastructure.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;
using FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

namespace FuaPay.Web.Modules.Reporting.Application;

internal sealed partial class AccountingReportBuilder
{
    private AccountingMovementRow[] ReadMovements(
        Dictionary<Guid, AccountingCardRow> acceptedPayments, Dictionary<Guid, AccountingCardRow> acceptedReturns)
    {
        var manual = data.ManualTopUps.ToDictionary(item => item.CommandId);
        var safeQ = data.SafeQTransfers.ToDictionary(item => item.CommandId);
        var adjustments = data.Adjustments.ToDictionary(item => item.CommandId);
        var returns = data.Returns.ToDictionary(item => item.Id);
        var printsByDebit = data.Prints.ToLookup(item => item.DebitOperationId);
        foreach (var print in data.Prints.Where(item => data.Includes(item.StateChangedAt)))
        {
            if (print.DebitOperationId is not { } id || !_movements.TryGetValue(id, out var debit) ||
                !MatchesPrint(print, debit) || printsByDebit[id].Count() != 1)
            {
                Problem("PRINT_DEBIT", "Zachycený tisk nemá jednoznačný odpovídající debit.", "PrintReservation", print.Id);
            }
        }

        foreach (var topUp in data.ManualTopUps.Where(item => data.Includes(item.AcceptedAt)))
        {
            if (!HasMovement(topUp.CommandId, topUp.OwnerId, CreditMovementType.Credit, topUp.AmountMinorUnits))
            {
                Problem("MANUAL_CREDIT", "Příkaz dobití nemá odpovídající kreditní pohyb.", "ManualCreditTopUp", topUp.CommandId);
            }
        }

        var results = new List<AccountingMovementRow>();
        foreach (var movement in data.Movements.Where(item => data.Includes(item.RecordedAt))
            .OrderBy(item => item.RecordedAt).ThenBy(item => item.AccountId).ThenBy(item => item.Sequence))
        {
            if (!_accounts.TryGetValue(movement.AccountId, out var account) || !TrySigned(movement, out var signed))
            {
                Problem("MOVEMENT", "Pohyb nemá platný účet, směr nebo částku.", "CreditMovement", movement.OperationId);
                continue;
            }

            var id = movement.OperationId;
            AccountingMovementKind kind;
            if (acceptedPayments.TryGetValue(id, out var payment) && payment.Wallet)
            {
                kind = AccountingMovementKind.CardTopUp;
            }
            else if (acceptedReturns.TryGetValue(id, out var cardReturn) && cardReturn.Wallet)
            {
                kind = AccountingMovementKind.CardTopUpReturn;
            }
            else if (manual.TryGetValue(id, out var topUp) && account.OwnerId == topUp.OwnerId &&
                signed == topUp.AmountMinorUnits && signed > 0 && topUp.AcceptedAt <= movement.RecordedAt)
            {
                _ = CheckDocument(FinancialDocumentSourceType.ManualCreditTopUp, id,
                    FinancialDocumentType.ManualCreditTopUp, account.OwnerId, signed, movement.RecordedAt,
                    jobId: null, legacyAllowed: !topUp.FinancialDocumentRequired);
                kind = AccountingMovementKind.ManualTopUp;
            }
            else if (safeQ.TryGetValue(id, out var transfer) && account.OwnerId == transfer.OwnerId &&
                signed == transfer.AmountMinorUnits && signed > 0 && transfer.AcceptedAt <= movement.RecordedAt)
            {
                kind = AccountingMovementKind.SafeQ;
            }
            else if (adjustments.TryGetValue(id, out var adjustment) && account.OwnerId == adjustment.OwnerId &&
                signed == adjustment.SignedAmountMinorUnits && adjustment.AcceptedAt <= movement.RecordedAt)
            {
                kind = AccountingMovementKind.Adjustment;
            }
            else if (signed < 0 && _jobs.TryGetValue(id, out var job) && IsCreditJob(job))
            {
                kind = AccountingMovementKind.Job;
            }
            else if (returns.TryGetValue(id, out var creditReturn) && IsCreditJobReturn(creditReturn))
            {
                kind = AccountingMovementKind.JobReturn;
            }
            else if (printsByDebit[id].Take(2).ToArray() is [var print] && MatchesPrint(print, movement))
            {
                var classified = classroomSource.HasValue && classroomSource.Value == print.PrintSourceId;
                if (!classified)
                {
                    Problem("PRINT_SOURCE", "Zachycený tisk nemá potvrzenou vazbu na učebnu.",
                        "PrintSource", print.PrintSourceId);
                }

                _centres.Add(new(id, movement.RecordedAt, $"print:{print.PrintSourceId}", null,
                    classified ? "Tisk v učebně" : "Tisk – nezařazený zdroj", "Zachycený tisk",
                    0, 0, movement.AmountMinorUnits, 0, print.PrintSourceId));
                kind = AccountingMovementKind.Print;
            }
            else
            {
                Problem("MOVEMENT_ORIGIN", "Původ pohybu nelze potvrdit.", "CreditMovement", id, signed);
                kind = AccountingMovementKind.Unknown;
            }

            results.Add(new(id, movement.RecordedAt, movement.Sequence, Party(account.OwnerId), kind,
                signed, movement.BalanceAfterMinorUnits, movement.Description));
        }

        return results.ToArray();
    }

    private (AccountingBalanceRow[] Rows, bool Complete) ReadBalances()
    {
        var byAccount = data.Movements.ToLookup(item => item.AccountId);
        var balances = new List<AccountingBalanceRow>();
        var complete = true;
        foreach (var account in data.Accounts.OrderBy(item => item.OwnerId))
        {
            if (!TryBalance(account, byAccount[account.Id], out var opening, out var closing, out var last))
            {
                Problem("LEDGER", "Chybí úplná souvislá historie nebo časový řez a zůstatky nesouhlasí.", "CreditAccount", account.Id);
                complete = false;
            }
            else if (last is not null && (opening != 0 || last.RecordedAt >= data.Period.From))
            {
                balances.Add(new(Party(account.OwnerId), last.RecordedAt, last.Description, opening, closing));
            }
        }

        if (data.Movements.Any(item => !_accounts.ContainsKey(item.AccountId)))
        {
            Problem("LEDGER_ACCOUNT", "Některý pohyb nemá účet.", "CreditMovement");
            complete = false;
        }

        return (balances.OrderBy(item => item.Party.Name, StringComparer.Ordinal).ToArray(), complete);
    }

    private bool TryBalance(CreditAccountEntity account, IEnumerable<CreditMovementEntity> movements,
        out long opening, out long closing, out CreditMovementEntity? last)
    {
        opening = 0;
        closing = 0;
        last = null;
        long running = 0;
        long sequence = 0;
        var passedOpening = false;
        var passedClosing = false;
        foreach (var item in movements.OrderBy(item => item.Sequence))
        {
            if (!TrySigned(item, out var signed))
            {
                return false;
            }

            running = checked(running + signed);
            if (item.Sequence != ++sequence || running < 0 || running != item.BalanceAfterMinorUnits)
            {
                return false;
            }

            if (item.RecordedAt < data.Period.From)
            {
                if (passedOpening)
                {
                    return false;
                }

                opening = running;
            }
            else
            {
                passedOpening = true;
            }

            if (item.RecordedAt < data.Cutoff)
            {
                if (passedClosing)
                {
                    return false;
                }

                closing = running;
                last = item;
            }
            else
            {
                passedClosing = true;
            }
        }

        return running == account.BalanceMinorUnits;
    }

    private static bool TrySigned(CreditMovementEntity movement, out long amount)
    {
        amount = movement.MovementType switch
        {
            (int)CreditMovementType.Credit => movement.AmountMinorUnits,
            (int)CreditMovementType.Debit => -movement.AmountMinorUnits,
            _ => 0
        };
        return movement.AmountMinorUnits > 0 && amount != 0;
    }

    private bool IsCreditJob(JobEntity job) => job.JobPaidAs(JobSettlementType.Credit) &&
        job.SettlementReferenceId == job.Id &&
        HasMovement(job.Id, job.CustomerUserId, CreditMovementType.Debit, job.PriceMinorUnits) &&
        _movements[job.Id].RecordedAt == job.SettledAt;

    private bool IsCreditJobReturn(SettlementReturnEntity row) =>
        row.Kind == (int)SettlementReturnKind.CreditJob && row.State == (int)SettlementReturnState.Completed &&
        row.Currency == Money.CurrencyCode && row.OriginalPaymentId is null &&
        row.JobId is { } jobId && _jobs.TryGetValue(jobId, out var job) && IsCreditJob(job) &&
        row.CustomerUserId == job.CustomerUserId && row.AmountMinorUnits == job.PriceMinorUnits &&
        HasMovement(row.Id, row.CustomerUserId, CreditMovementType.Credit, row.AmountMinorUnits) &&
        row.CompletedAt >= _movements[row.Id].RecordedAt && _movements[row.Id].RecordedAt >= job.SettledAt;

    private static bool MatchesPrint(PrintReservationEntity print, CreditMovementEntity movement) =>
        print.Status == (int)PrintReservationStatus.Captured && print.PrintSourceId != Guid.Empty &&
        print.DebitOperationId == movement.OperationId && print.CreditAccountId == movement.AccountId &&
        movement.MovementType == (int)CreditMovementType.Debit && print.AmountMinorUnits == movement.AmountMinorUnits &&
        print.AmountMinorUnits > 0 && print.StateChangedAt == movement.RecordedAt;

    private AccountingJobRow[] ReadJobs(
        Dictionary<Guid, AccountingCardRow> acceptedPayments, Dictionary<Guid, AccountingCardRow> acceptedReturns)
    {
        var units = data.Units.ToDictionary(item => item.Id);
        var rows = new List<AccountingJobRow>();
        foreach (var job in data.Jobs.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id))
        {
            units.TryGetValue(job.ServiceUnitId, out var unit);
            var paidCredit = IsCreditJob(job);
            var paidCard = job.JobPaidAs(JobSettlementType.DirectPayment) && job.SettlementReferenceId is { } paymentId &&
                acceptedPayments.TryGetValue(paymentId, out var paid) && !paid.Wallet &&
                _payments[paymentId].JobId == job.Id && IsDirectJob(job, _payments[paymentId]);
            var paidAsOf = job.SettledAt < data.Cutoff && (paidCredit || paidCard);
            if (data.Includes(job.CreatedAt))
            {
                var unknown = !paidAsOf && job.SettledAt < data.Cutoff;
                rows.Add(new(job, unit, Party(job.CustomerUserId),
                    paidAsOf ? "Uhrazeno" : unknown ? "Neověřená úhrada" : "Neuhrazeno", paidAsOf));
            }

            if (!data.Includes(job.SettledAt))
            {
                continue;
            }

            if (!paidAsOf)
            {
                Problem("JOB_SETTLEMENT", "Úhrada zakázky nemá doloženou finanční vazbu.", "Job", job.Id, job.PriceMinorUnits);
                continue;
            }

            AddJobCentre(job, unit, job.Id, job.SettledAt!.Value,
                paidCard ? "Zakázka kartou" : "Zakázka kreditem",
                paidCard ? job.PriceMinorUnits : 0, paidCredit ? job.PriceMinorUnits : 0, 0);
        }

        foreach (var row in data.Returns.Where(item => item.State == (int)SettlementReturnState.Completed &&
            item.Kind is (int)SettlementReturnKind.CreditJob or (int)SettlementReturnKind.CardJob &&
            data.Includes(item.CompletedAt)))
        {
            var valid = row.Kind == (int)SettlementReturnKind.CardJob
                ? acceptedReturns.ContainsKey(row.Id) : IsCreditJobReturn(row);
            if (!valid || row.JobId is not { } jobId || !_jobs.TryGetValue(jobId, out var job))
            {
                Problem("CENTRE_RETURN", "Vratku služby nelze přiřadit k původní úhradě.", "SettlementReturn", row.Id, row.AmountMinorUnits);
                continue;
            }

            units.TryGetValue(job.ServiceUnitId, out var unit);
            AddJobCentre(job, unit, row.Id, row.CompletedAt!.Value,
                row.Kind == (int)SettlementReturnKind.CardJob ? "Vratka zakázky kartou" : "Vratka zakázky kreditem",
                0, 0, row.AmountMinorUnits);
        }

        return rows.ToArray();
    }

    private void AddJobCentre(JobEntity job, ServiceUnitEntity? unit,
        Guid eventId, DateTimeOffset eventAt, string kind, long card, long credit, long returned)
    {
        if (unit is null)
        {
            Problem("JOB_UNIT", "Zakázce chybí dohledatelné pracoviště.", "Job", job.Id);
        }

        _centres.Add(new(eventId, eventAt, $"job:{job.ServiceUnitId}", unit?.Code,
            unit?.DisplayName ?? "Nepřiřazené pracoviště", kind, card, credit, 0, returned, job.Id));
    }
}
