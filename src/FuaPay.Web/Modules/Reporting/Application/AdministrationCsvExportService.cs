using System.Globalization;
using System.Text;

using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Modules.Reporting.Application;

public sealed class AdministrationCsvExportService
{
    private const int PageSize = 100;
    private const int MaximumRows = 100_000;

    private static readonly CultureInfo CzechCulture =
        CultureInfo.GetCultureInfo("cs-CZ");

    private readonly IJobQueries _jobQueries;
    private readonly ICreditQueries _creditQueries;
    private readonly IPaymentQueries _paymentQueries;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly IServiceUnitQueries _serviceUnitQueries;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;

    public AdministrationCsvExportService(
        IJobQueries jobQueries,
        ICreditQueries creditQueries,
        IPaymentQueries paymentQueries,
        IAccessUserQueries accessUserQueries,
        IServiceUnitQueries serviceUnitQueries,
        TimeProvider timeProvider,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(creditQueries);
        ArgumentNullException.ThrowIfNull(paymentQueries);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);

        _jobQueries = jobQueries;
        _creditQueries = creditQueries;
        _paymentQueries = paymentQueries;
        _accessUserQueries = accessUserQueries;
        _serviceUnitQueries = serviceUnitQueries;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
    }

    public async Task<CsvExportFile> ExportJobsAsync(
        Guid administratorUserId,
        Guid? serviceUnitId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        ValidateAdministrator(administratorUserId);
        var range = CreateUtcRange(from, to);
        var actor = serviceUnitId.HasValue
            ? new JobManagementActor(
                administratorUserId,
                new[] { serviceUnitId.Value })
            : new JobManagementActor(
                administratorUserId,
                JobManagementScope.All);

        var items = await ReadAllJobsAsync(
            actor,
            new JobListFilter(
                serviceUnitId: serviceUnitId,
                createdFrom: range.From,
                createdToExclusive: range.ToExclusive),
            cancellationToken);

        var users = await _accessUserQueries.FindOptionsAsync(
            items.SelectMany(
                item => new[]
                {
                    item.CustomerUserId,
                    item.CreatedByUserId
                }),
            cancellationToken);

        var units = (await _serviceUnitQueries.ListAllAsync(
                cancellationToken))
            .ToDictionary(item => item.Id);

        var rows = new List<IReadOnlyList<string?>>
        {
            new[]
            {
                "Číslo zakázky",
                "Pracoviště",
                "Kód pracoviště",
                "Druh služby",
                "Zákazník",
                "E-mail zákazníka",
                "Založil",
                "Název",
                "Cena Kč",
                "Výrobní stav",
                "Stav úhrady",
                "Vytvořeno",
                "Zveřejněno",
                "Uhrazeno"
            }
        };

        rows.AddRange(
            items.Select(
                item =>
                {
                    users.TryGetValue(item.CustomerUserId, out var customer);
                    users.TryGetValue(item.CreatedByUserId, out var creator);
                    units.TryGetValue(item.ServiceUnitId, out var unit);

                    return (IReadOnlyList<string?>)new[]
                    {
                        item.Number,
                        unit?.DisplayName,
                        unit?.Code,
                        item.ServiceType.ToString(),
                        customer?.DisplayName,
                        customer?.Email,
                        creator?.DisplayName,
                        item.Title,
                        FormatMoney(item.PriceMinorUnits),
                        item.ProductionStatus.ToString(),
                        item.PaymentStatus.ToString(),
                        FormatTimestamp(item.CreatedAt),
                        FormatTimestamp(item.PublishedAt),
                        FormatTimestamp(item.SettledAt)
                    };
                }));

        var file = CreateFile("zakazky", rows);
        await WriteExportAuditAsync(
            administratorUserId,
            "export.jobs",
            file,
            items.Count,
            cancellationToken);
        return file;
    }

    public async Task<CsvExportFile> ExportCreditMovementsAsync(
        Guid administratorUserId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        ValidateAdministrator(administratorUserId);
        var range = CreateUtcRange(from, to);
        var items = await ReadAllCreditMovementsAsync(
            new CreditAdministrationMovementFilter(
                recordedFrom: range.From,
                recordedToExclusive: range.ToExclusive),
            cancellationToken);

        var users = await _accessUserQueries.FindOptionsAsync(
            items.Select(item => item.OwnerId),
            cancellationToken);

        var rows = new List<IReadOnlyList<string?>>
        {
            new[]
            {
                "Datum",
                "Uživatel",
                "E-mail",
                "Druh pohybu",
                "Částka Kč",
                "Zůstatek po pohybu Kč",
                "Popis",
                "ID operace"
            }
        };

        rows.AddRange(
            items.Select(
                item =>
                {
                    users.TryGetValue(item.OwnerId, out var owner);
                    return (IReadOnlyList<string?>)new[]
                    {
                        FormatTimestamp(item.RecordedAt),
                        owner?.DisplayName,
                        owner?.Email,
                        item.Type.ToString(),
                        FormatMoney(item.AmountMinorUnits),
                        FormatMoney(item.BalanceAfterMinorUnits),
                        item.Description,
                        item.OperationId.ToString()
                    };
                }));

        var file = CreateFile("kreditni-pohyby", rows);
        await WriteExportAuditAsync(
            administratorUserId,
            "export.credit-movements",
            file,
            items.Count,
            cancellationToken);
        return file;
    }

    public async Task<CsvExportFile> ExportPaymentsAsync(
        Guid administratorUserId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        ValidateAdministrator(administratorUserId);
        var range = CreateUtcRange(from, to);
        var items = await ReadAllPaymentsAsync(
            new PaymentListFilter(
                createdFrom: range.From,
                createdToExclusive: range.ToExclusive),
            cancellationToken);

        var users = await _accessUserQueries.FindOptionsAsync(
            items.Select(item => item.CustomerUserId),
            cancellationToken);

        var rows = new List<IReadOnlyList<string?>>
        {
            new[]
            {
                "Vytvořeno",
                "Dokončeno",
                "Zákazník",
                "E-mail",
                "Účel",
                "Částka Kč",
                "Poskytovatel",
                "Stav",
                "Reference poskytovatele",
                "ID zakázky",
                "Důvod selhání",
                "ID platby"
            }
        };

        rows.AddRange(
            items.Select(
                item =>
                {
                    users.TryGetValue(item.CustomerUserId, out var customer);
                    return (IReadOnlyList<string?>)new[]
                    {
                        FormatTimestamp(item.CreatedAt),
                        FormatTimestamp(item.CompletedAt),
                        customer?.DisplayName,
                        customer?.Email,
                        item.PurposeType.ToString(),
                        FormatMoney(item.AmountMinorUnits),
                        item.Provider.ToString(),
                        item.Status.ToString(),
                        item.ProviderReference,
                        item.JobId?.ToString(),
                        item.FailureReason,
                        item.Id.ToString()
                    };
                }));

        var file = CreateFile("platby", rows);
        await WriteExportAuditAsync(
            administratorUserId,
            "export.payments",
            file,
            items.Count,
            cancellationToken);
        return file;
    }


    private Task WriteExportAuditAsync(
        Guid administratorUserId,
        string action,
        CsvExportFile file,
        int rowCount,
        CancellationToken cancellationToken)
    {
        return _auditTrail.WriteAsync(AuditEntry.ForUser(
            administratorUserId,
            action,
            "csv-export",
            file.FileName,
            $"Byl vytvořen export {file.FileName} s {rowCount} datovými řádky.",
            _timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private async Task<IReadOnlyList<JobListItem>> ReadAllJobsAsync(
        JobManagementActor actor,
        JobListFilter filter,
        CancellationToken cancellationToken)
    {
        var result = new List<JobListItem>();

        for (var offset = 0; offset < MaximumRows; offset += PageSize)
        {
            var page = await _jobQueries.ListForManagementAsync(
                actor,
                filter,
                new JobPageRequest(offset, PageSize),
                cancellationToken);
            result.AddRange(page.Items);

            if (!page.HasMore)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"Export překročil bezpečnostní limit {MaximumRows:N0} řádků.");
    }

    private async Task<IReadOnlyList<CreditAdministrationMovementListItem>>
        ReadAllCreditMovementsAsync(
            CreditAdministrationMovementFilter filter,
            CancellationToken cancellationToken)
    {
        var result = new List<CreditAdministrationMovementListItem>();

        for (var offset = 0; offset < MaximumRows; offset += PageSize)
        {
            var page = await _creditQueries.ListAdministrationMovementsAsync(
                filter,
                new CreditMovementPageRequest(offset, PageSize),
                cancellationToken);
            result.AddRange(page.Items);

            if (!page.HasMore)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"Export překročil bezpečnostní limit {MaximumRows:N0} řádků.");
    }

    private async Task<IReadOnlyList<PaymentListItem>> ReadAllPaymentsAsync(
        PaymentListFilter filter,
        CancellationToken cancellationToken)
    {
        var result = new List<PaymentListItem>();

        for (var offset = 0; offset < MaximumRows; offset += PageSize)
        {
            var page = await _paymentQueries.ListForAdministrationAsync(
                filter,
                new PaymentPageRequest(offset, PageSize),
                cancellationToken);
            result.AddRange(page.Items);

            if (!page.HasMore)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"Export překročil bezpečnostní limit {MaximumRows:N0} řádků.");
    }

    private CsvExportFile CreateFile(
        string prefix,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        builder.Append('\uFEFF');

        foreach (var row in rows)
        {
            builder.AppendLine(
                string.Join(
                    ';',
                    row.Select(EscapeCsvValue)));
        }

        var timestamp = _timeProvider.GetUtcNow()
            .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        return new CsvExportFile(
            $"fua-pay-{prefix}-{timestamp}.csv",
            Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string EscapeCsvValue(string? value)
    {
        var safe = value ?? string.Empty;

        if (
            safe.Length > 0 &&
            safe[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            safe = "'" + safe;
        }

        return '"' + safe.Replace("\"", "\"\"") + '"';
    }

    private static string FormatMoney(long minorUnits)
    {
        return (minorUnits / 100m).ToString("0.00", CzechCulture);
    }

    private static string? FormatTimestamp(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    private static (DateTimeOffset? From, DateTimeOffset? ToExclusive)
        CreateUtcRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && to.Value < from.Value)
        {
            throw new ArgumentException(
                "Konec období nesmí předcházet jeho začátku.",
                nameof(to));
        }

        return (
            from.HasValue
                ? new DateTimeOffset(
                    from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            to.HasValue
                ? new DateTimeOffset(
                    to.Value.AddDays(1).ToDateTime(
                        TimeOnly.MinValue,
                        DateTimeKind.Utc))
                : null);
    }

    private static void ValidateAdministrator(Guid administratorUserId)
    {
        if (administratorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID administrátora nesmí být prázdné.",
                nameof(administratorUserId));
        }
    }
}
