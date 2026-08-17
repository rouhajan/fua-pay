using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;

internal sealed class EfJobNumberAllocator : IJobNumberAllocator
{
    private const int MaximumValue = 999999;

    private static readonly Regex ServiceUnitCodePattern = new(
        "^[A-Z0-9]{2,8}$",
        RegexOptions.CultureInvariant);

    private readonly FuaPayDbContext _dbContext;

    public EfJobNumberAllocator(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<string> AllocateAsync(
        Guid serviceUnitId,
        string serviceUnitCode,
        int year,
        CancellationToken cancellationToken = default)
    {
        Validate(serviceUnitId, year);

        if (string.IsNullOrWhiteSpace(serviceUnitCode))
        {
            throw new ArgumentException(
                "Kód pracoviště nesmí být prázdný.",
                nameof(serviceUnitCode));
        }

        var normalizedCode =
            serviceUnitCode.Trim().ToUpperInvariant();

        if (!ServiceUnitCodePattern.IsMatch(normalizedCode))
        {
            throw new ArgumentException(
                "Kód pracoviště musí obsahovat 2 až 8 " +
                "velkých písmen nebo číslic.",
                nameof(serviceUnitCode));
        }

        var value = await ExecuteScalarAsync(
            """
            INSERT INTO jobs.job_number_sequences
                (service_unit_id, year, last_value)
            VALUES
                (@serviceUnitId, @year, 1)
            ON CONFLICT (service_unit_id, year)
            DO UPDATE SET last_value =
                jobs.job_number_sequences.last_value + 1
            WHERE jobs.job_number_sequences.last_value < 999999
            RETURNING last_value;
            """,
            serviceUnitId,
            year,
            cancellationToken);

        if (!value.HasValue)
        {
            throw new InvalidOperationException(
                $"Číselná řada pracoviště '{normalizedCode}' " +
                $"pro rok {year} je vyčerpaná.");
        }

        return $"{normalizedCode}-{year:D4}-{value.Value:D6}";
    }

    public async Task EnsureAtLeastAsync(
        Guid serviceUnitId,
        int year,
        int value,
        CancellationToken cancellationToken = default)
    {
        Validate(serviceUnitId, year);

        if (value <= 0 || value > MaximumValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value));
        }

        await ExecuteScalarAsync(
            """
            INSERT INTO jobs.job_number_sequences
                (service_unit_id, year, last_value)
            VALUES
                (@serviceUnitId, @year, @value)
            ON CONFLICT (service_unit_id, year)
            DO UPDATE SET last_value = GREATEST(
                jobs.job_number_sequences.last_value,
                EXCLUDED.last_value)
            RETURNING last_value;
            """,
            serviceUnitId,
            year,
            cancellationToken,
            value);
    }

    private async Task<int?> ExecuteScalarAsync(
        string sql,
        Guid serviceUnitId,
        int year,
        CancellationToken cancellationToken,
        int? value = null)
    {
        var connection =
            _dbContext.Database.GetDbConnection();

        var closeAfter = connection.State != ConnectionState.Open;

        if (closeAfter)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _dbContext.Database
                .CurrentTransaction?
                .GetDbTransaction();

            AddParameter(
                command,
                "serviceUnitId",
                serviceUnitId);

            AddParameter(command, "year", year);

            if (value.HasValue)
            {
                AddParameter(command, "value", value.Value);
            }

            var result = await command.ExecuteScalarAsync(
                cancellationToken);

            return result is null or DBNull
                ? null
                : Convert.ToInt32(result);
        }
        finally
        {
            if (closeAfter)
            {
                await connection.CloseAsync();
            }
        }
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

    private static void Validate(
        Guid serviceUnitId,
        int year)
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        if (year < 2000 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }
    }
}
