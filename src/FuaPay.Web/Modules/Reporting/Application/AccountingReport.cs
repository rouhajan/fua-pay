using FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;
using FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

namespace FuaPay.Web.Modules.Reporting.Application;

internal enum AccountingMovementKind
{
    CardTopUp, CardTopUpReturn, SafeQ, Job, JobReturn, Print, ManualTopUp, Adjustment, Unknown
}

internal sealed record AccountingParty(string Name, string? Email);
internal sealed record AccountingCardRow(
    Guid Id, Guid PaymentId, DateTimeOffset At, string Operation, string? DocumentNumber,
    Guid? DocumentId, string PayId, long OrderNumber, long Received, long Returned, bool Wallet);
internal sealed record AccountingMovementRow(
    Guid Id, DateTimeOffset At, long Sequence, AccountingParty Party, AccountingMovementKind Kind,
    long SignedAmount, long Balance, string Description);
internal sealed record AccountingBalanceRow(
    AccountingParty Party, DateTimeOffset LastAt, string LastDescription, long Opening, long Closing);
internal sealed record AccountingJobRow(JobEntity Job, ServiceUnitEntity? Unit, AccountingParty Party, string PaymentStatus,
    bool Paid);
internal sealed record AccountingCentreEvent(Guid Id, DateTimeOffset At, string Key, string? UnitCode, string Name,
    string Kind, long Card, long Credit, long Print, long Returned, Guid SourceId);
internal sealed record AccountingProblem(string Code, string Reason, string Source, Guid? Id = null, long? Amount = null);

internal sealed record AccountingReport(
    AccountingPeriod Period, DateTimeOffset CapturedAt, DateTimeOffset Cutoff, AccountingRawTotals RawTotals,
    AccountingCardRow[] Card,
    AccountingMovementRow[] Movements, AccountingBalanceRow[] Balances, AccountingJobRow[] Jobs,
    AccountingCentreEvent[] Centres, ServiceUnitEntity[] Units, AccountingProblem[] Problems, bool LedgerComplete, int CarriedAccounts,
    int UnsuccessfulAttempts, int OpenReturns, int VerifiedDocuments, int CrossBoundaryEffects)
{
    public long Received => Card.Sum(item => item.Received);
    public long Returned => Card.Sum(item => item.Returned);
    public long NetCard => checked(Received - Returned);
    public bool CardAmountsIncomplete =>
        RawTotals.Payments != Card.Count(item => item.Received > 0) ||
        RawTotals.Returns != Card.Count(item => item.Returned > 0) ||
        RawTotals.Receipts != Received || RawTotals.ReturnAmounts != Returned ||
        Problems.Any(item => item.Code == "RECONCILIATION");
    public bool CreditBreakdownIncomplete => Problems.Any(item =>
        item.Code is "MOVEMENT" or "MOVEMENT_ORIGIN" or "MANUAL_CREDIT" or "PRINT_DEBIT");
    public bool CentreAmountsIncomplete => Problems.Any(item =>
        item.Code is "JOB_SETTLEMENT" or "CENTRE_RETURN" or "PRINT_DEBIT");
    public bool FeeModelAvailable => !CardAmountsIncomplete && Period.IsClosedMonth(CapturedAt);
    public long Opening => Balances.Sum(item => item.Opening);
    public long Closing => Balances.Sum(item => item.Closing);
    public long MovementTotal => Movements.Sum(item => item.SignedAmount);
    public long MovementValue(AccountingMovementKind kind) =>
        Movements.Where(item => item.Kind == kind).Sum(item => item.SignedAmount);
}
