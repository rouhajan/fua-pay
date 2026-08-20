using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Modules.Receipts.Application;

public sealed class JobPaymentReceiptService
{
    private readonly IJobQueries _jobQueries;
    private readonly ICreditQueries _creditQueries;
    private readonly IPaymentQueries _paymentQueries;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly IServiceUnitQueries _serviceUnitQueries;
    private readonly ReceiptConfiguration _configuration;

    public JobPaymentReceiptService(
        IJobQueries jobQueries,
        ICreditQueries creditQueries,
        IPaymentQueries paymentQueries,
        IAccessUserQueries accessUserQueries,
        IServiceUnitQueries serviceUnitQueries,
        ReceiptConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(creditQueries);
        ArgumentNullException.ThrowIfNull(paymentQueries);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        ArgumentNullException.ThrowIfNull(configuration);

        _jobQueries = jobQueries;
        _creditQueries = creditQueries;
        _paymentQueries = paymentQueries;
        _accessUserQueries = accessUserQueries;
        _serviceUnitQueries = serviceUnitQueries;
        _configuration = configuration;
    }

    public async Task<JobPaymentReceiptData?> CreateForCustomerJobAsync(
        Guid customerUserId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(customerUserId, nameof(customerUserId));
        ValidateId(jobId, nameof(jobId));

        if (!_configuration.Enabled)
        {
            return null;
        }

        var job = await _jobQueries.FindForCustomerAsync(
            customerUserId,
            jobId,
            cancellationToken);

        if (job is null || job.PaymentStatus != JobPaymentStatus.Paid)
        {
            return null;
        }

        var settlementType = job.SettlementType
            ?? throw Inconsistent(job, "chybí typ úhrady");
        var settlementReferenceId = job.SettlementReferenceId
            ?? throw Inconsistent(job, "chybí reference úhrady");
        var settledAt = job.SettledAt
            ?? throw Inconsistent(job, "chybí čas úhrady");

        var users = await _accessUserQueries.FindOptionsAsync(
            [job.CustomerUserId],
            cancellationToken);
        if (!users.TryGetValue(job.CustomerUserId, out var customer))
        {
            throw Inconsistent(job, "zákazník není dohledatelný");
        }

        var serviceUnits = await _serviceUnitQueries.ListAllAsync(
            cancellationToken);
        var serviceUnit = serviceUnits.SingleOrDefault(
            item => item.Id == job.ServiceUnitId)
            ?? throw Inconsistent(job, "pracoviště není dohledatelné");

        var settlement = settlementType switch
        {
            JobSettlementType.Credit => await ValidateCreditSettlementAsync(
                job,
                settlementReferenceId,
                settledAt,
                cancellationToken),
            JobSettlementType.DirectPayment =>
                await ValidateDirectPaymentSettlementAsync(
                    job,
                    settlementReferenceId,
                    cancellationToken),
            _ => throw Inconsistent(job, "typ úhrady není podporovaný")
        };

        var taxBaseMinorUnits = CalculateTaxBaseMinorUnits(
            job.PriceMinorUnits,
            _configuration.VatRatePercent);
        var vatAmountMinorUnits = checked(
            job.PriceMinorUnits - taxBaseMinorUnits);

        return new JobPaymentReceiptData(
            ReceiptReference: $"PAY-{job.Number}",
            JobId: job.Id,
            JobNumber: job.Number,
            JobTitle: job.Title,
            ServiceUnitCode: serviceUnit.Code,
            ServiceUnitName: serviceUnit.DisplayName,
            CustomerName: customer.DisplayName,
            CustomerEmail: customer.Email,
            GrossAmountMinorUnits: job.PriceMinorUnits,
            TaxBaseMinorUnits: taxBaseMinorUnits,
            VatAmountMinorUnits: vatAmountMinorUnits,
            VatRatePercent: _configuration.VatRatePercent,
            SettledAt: settledAt,
            SettlementMethod: settlement.Method,
            PaymentProvider: settlement.Provider,
            ProviderReference: settlement.ProviderReference,
            SettlementReferenceId: settlementReferenceId,
            Issuer: _configuration.Issuer,
            PreviewMode: _configuration.PreviewMode);
    }

    private async Task<ReceiptSettlementDetails>
        ValidateCreditSettlementAsync(
            JobDetail job,
            Guid settlementReferenceId,
            DateTimeOffset settledAt,
            CancellationToken cancellationToken)
    {
        if (settlementReferenceId != job.Id)
        {
            throw Inconsistent(
                job,
                "reference kreditní úhrady neodpovídá ID zakázky");
        }

        var movement = await _creditQueries.FindMovementForOwnerAsync(
            job.CustomerUserId,
            settlementReferenceId,
            cancellationToken);

        if (movement is null)
        {
            throw Inconsistent(
                job,
                "kreditní pohyb úhrady není dohledatelný");
        }

        if (movement.Type != CreditMovementType.Debit)
        {
            throw Inconsistent(
                job,
                "reference úhrady neukazuje na kreditní debet");
        }

        if (movement.AmountMinorUnits != job.PriceMinorUnits)
        {
            throw Inconsistent(
                job,
                "částka kreditního debetu neodpovídá ceně zakázky");
        }

        if (movement.RecordedAt != settledAt)
        {
            throw Inconsistent(
                job,
                "čas kreditního debetu neodpovídá času úhrady zakázky");
        }

        return new ReceiptSettlementDetails(
            "Kredit FUA Pay",
            null,
            null);
    }

    private async Task<ReceiptSettlementDetails>
        ValidateDirectPaymentSettlementAsync(
            JobDetail job,
            Guid settlementReferenceId,
            CancellationToken cancellationToken)
    {
        var payment = await _paymentQueries.FindForCustomerAsync(
            job.CustomerUserId,
            settlementReferenceId,
            cancellationToken);

        if (payment is null)
        {
            throw Inconsistent(
                job,
                "přímá platba úhrady není dohledatelná");
        }

        if (
            payment.Status != PaymentStatus.Succeeded ||
            payment.PurposeType != PaymentPurposeType.Job ||
            payment.JobId != job.Id ||
            payment.AmountMinorUnits != job.PriceMinorUnits ||
            string.IsNullOrWhiteSpace(payment.ProviderReference) ||
            payment.CompletedAt is null)
        {
            throw Inconsistent(
                job,
                "přímá platba neodpovídá uhrazené zakázce");
        }

        return new ReceiptSettlementDetails(
            "Přímá platba",
            PaymentProviderLabel(payment.Provider),
            payment.ProviderReference);
    }

    private static long CalculateTaxBaseMinorUnits(
        long grossAmountMinorUnits,
        int vatRatePercent)
    {
        if (grossAmountMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grossAmountMinorUnits));
        }

        if (vatRatePercent == 0)
        {
            return grossAmountMinorUnits;
        }

        var taxBase = decimal.Round(
            grossAmountMinorUnits * 100m / (100m + vatRatePercent),
            0,
            MidpointRounding.AwayFromZero);

        return checked((long)taxBase);
    }

    private static string PaymentProviderLabel(PaymentProvider provider) =>
        provider switch
        {
            PaymentProvider.Development => "Vývojový poskytovatel",
            PaymentProvider.Csob => "ČSOB",
            _ => throw new ReceiptConsistencyException(
                "Úspěšná přímá platba má neznámého poskytovatele.")
        };

    private static ReceiptConsistencyException Inconsistent(
        JobDetail job,
        string detail) =>
        new(
            $"Nelze sestavit doklad pro zakázku {job.Number}: {detail}.");

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "ID nesmí být prázdné.",
                parameterName);
        }
    }

    private sealed record ReceiptSettlementDetails(
        string Method,
        string? Provider,
        string? ProviderReference);
}
