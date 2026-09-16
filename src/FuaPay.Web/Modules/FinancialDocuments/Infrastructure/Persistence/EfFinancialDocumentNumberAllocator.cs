using System.Data.Common;
using System.Globalization;

using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Application;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

internal sealed class EfFinancialDocumentNumberAllocator :
    IFinancialDocumentNumberAllocator
{
    private const int MinimumBusinessYear = 2000;
    private const int MaximumSequence = 999999;

    private static readonly TimeZoneInfo PragueTimeZone =
        ResolvePragueTimeZone();

    private readonly FuaPayDbContext _dbContext;

    public EfFinancialDocumentNumberAllocator(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<FinancialDocumentNumberAllocation> AllocateAsync(
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken = default)
    {
        if (issuedAt == default)
        {
            throw new ArgumentException(
                "Čas vystavení nesmí být prázdný.",
                nameof(issuedAt));
        }

        var businessYear = TimeZoneInfo
            .ConvertTime(issuedAt, PragueTimeZone)
            .Year;

        if (businessYear < MinimumBusinessYear)
        {
            throw new ArgumentOutOfRangeException(nameof(issuedAt));
        }

        var connectionString =
            _dbContext.Database.GetConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string pro číselnou řadu dokladů není dostupný.");
        }

        var allocationConnectionString =
            new NpgsqlConnectionStringBuilder(connectionString)
            {
                Enlist = false
            }.ConnectionString;

        await using var connection =
            new NpgsqlConnection(allocationConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO financial_documents.number_counters
                (business_year, last_value)
            VALUES
                (@businessYear, 1)
            ON CONFLICT (business_year)
            DO UPDATE SET last_value =
                financial_documents.number_counters.last_value + 1
            WHERE financial_documents.number_counters.last_value < 999999
            RETURNING last_value;
            """;
        AddParameter(command, "businessYear", businessYear);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (result is null or DBNull)
        {
            throw new InvalidOperationException(
                $"Číselná řada finančních dokladů pro rok " +
                $"{businessYear} je vyčerpaná.");
        }

        var sequence = Convert.ToInt32(
            result,
            CultureInfo.InvariantCulture);

        if (sequence is < 1 or > MaximumSequence)
        {
            throw new InvalidDataException(
                "Databáze vrátila neplatnou hodnotu číselné řady dokladů.");
        }

        return new FinancialDocumentNumberAllocation(
            $"FUA-{businessYear:D4}-{sequence:D6}",
            businessYear,
            sequence);
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static TimeZoneInfo ResolvePragueTimeZone()
    {
        foreach (
            var identifier in
            new[] { "Europe/Prague", "Central Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(identifier);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException(
            "Systém neobsahuje časovou zónu Europe/Prague " +
            "potřebnou pro číslování finančních dokladů.");
    }
}
