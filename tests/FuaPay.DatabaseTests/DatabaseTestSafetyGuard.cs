using System.Net;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Configuration;

using Npgsql;

namespace FuaPay.DatabaseTests;

internal static class DatabaseTestSafetyGuard
{
    private const string ExplicitOptInVariable =
        "FUA_PAY_DATABASE_TESTS_ALLOWED";

    [ModuleInitializer]
    internal static void ValidateBeforeTestDiscovery()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("FuaPay");

        Validate(
            connectionString,
            Environment.GetEnvironmentVariable(
                ExplicitOptInVariable));
    }

    internal static void Validate(
        string? connectionString,
        string? explicitOptIn)
    {
        if (!string.Equals(
                explicitOptIn,
                "1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Databázové testy jsou z bezpečnostních důvodů " +
                $"zakázané. Nastav {ExplicitOptInVariable}=1 pouze " +
                "pro samostatnou lokální testovací nebo auditní " +
                "databázi.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Databázové testy vyžadují connection string " +
                "ConnectionStrings:FuaPay.");
        }

        NpgsqlConnectionStringBuilder builder;

        try
        {
            builder = new NpgsqlConnectionStringBuilder(
                connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:FuaPay není platný PostgreSQL " +
                "connection string.",
                exception);
        }

        if (!IsLoopbackHost(builder.Host))
        {
            throw new InvalidOperationException(
                "Databázové testy smějí běžet pouze proti " +
                "PostgreSQL na loopback rozhraní " +
                "(localhost, 127.0.0.1 nebo ::1).");
        }

        if (!IsAllowedDatabaseName(builder.Database))
        {
            throw new InvalidOperationException(
                "Název databáze pro integrační testy musí začínat " +
                "fuapay_test nebo fuapay_audit. Běžná vývojová, " +
                "stagingová ani produkční databáze není povolená.");
        }
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var normalizedHost = host.Trim();

        if (string.Equals(
                normalizedHost,
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(
                normalizedHost,
                out var address) &&
            IPAddress.IsLoopback(address);
    }

    private static bool IsAllowedDatabaseName(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        return HasAllowedPrefix(
                databaseName,
                "fuapay_test") ||
            HasAllowedPrefix(
                databaseName,
                "fuapay_audit");
    }

    private static bool HasAllowedPrefix(
        string databaseName,
        string allowedPrefix)
    {
        return string.Equals(
                databaseName,
                allowedPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            databaseName.StartsWith(
                allowedPrefix + "_",
                StringComparison.OrdinalIgnoreCase);
    }
}
