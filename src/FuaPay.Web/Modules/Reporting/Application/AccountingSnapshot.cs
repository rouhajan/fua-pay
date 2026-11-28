using FuaPay.Web.Modules.Access.Infrastructure.Persistence;
using FuaPay.Web.Modules.Credits.Infrastructure.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;
using FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;
using FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

namespace FuaPay.Web.Modules.Reporting.Application;

internal sealed record AccountingSnapshot(
    AccountingPeriod Period, DateTimeOffset CapturedAt, DateTimeOffset Cutoff,
    AccountingRawTotals RawTotals,
    CreditAccountEntity[] Accounts, CreditMovementEntity[] Movements, PaymentEntity[] Payments,
    AccountingInitiation[] Initiations, AccountingRecovery[] Recoveries, SettlementReturnEntity[] Returns,
    SettlementReturnProviderAttemptEntity[] Attempts, JobEntity[] Jobs, ServiceUnitEntity[] Units,
    FinancialDocumentEntity[] Documents, ManualCreditTopUpCommandEntity[] ManualTopUps,
    LegacySafeQCreditTransferEntity[] SafeQTransfers, CreditAdjustmentCommandEntity[] Adjustments,
    PrintReservationEntity[] Prints, AccessUserEntity[] Users)
{
    public bool Includes(DateTimeOffset? time) => time >= Period.From && time < Cutoff;
}

internal sealed record AccountingRawTotals(int Payments, long Receipts, int Returns, long ReturnAmounts);

internal sealed record AccountingInitiation(Guid PaymentId, int Provider, int State, long OrderNumber);
internal sealed record AccountingRecovery(Guid PaymentId, int State);

internal sealed class AccountingWorkbookLimitException : Exception;
internal sealed class AccountingWorkbookFuturePeriodException : Exception;
