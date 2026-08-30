namespace FuaPay.DeploymentArtifacts;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidDataException or
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"Deployment artifact error: {exception.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            return WriteUsage();
        }

        var options = ParseOptions(args.Skip(2));

        return (args[0], args[1]) switch
        {
            ("release", "create") => CreateRelease(options),
            ("release", "verify") => VerifyRelease(options),
            ("migrations", "prepare") => PrepareMigrations(options),
            ("migrations", "verify") => VerifyMigrations(options),
            _ => WriteUsage()
        };
    }

    private static int CreateRelease(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(options, "source", "archive");
        var report = ReleaseArchive.Create(
            Require(options, "source"),
            Require(options, "archive"));

        WriteReleaseReport(report);
        return 0;
    }

    private static int VerifyRelease(IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(options, "archive");
        var report = ReleaseArchive.Verify(Require(options, "archive"));
        WriteReleaseReport(report);
        return 0;
    }

    private static int PrepareMigrations(
        IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(options, "input", "output", "role");
        var result = MigrationSqlArtifact.Prepare(
            Require(options, "input"),
            Require(options, "output"),
            Require(options, "role"));

        Console.WriteLine(
            $"Leading UTF-8 BOM removed: {result.HadLeadingBom}");
        Console.WriteLine(
            $"Original SHA-256: {result.OriginalSha256}");
        Console.WriteLine(
            $"Execution SHA-256: {result.ExecutionSha256}");
        return 0;
    }

    private static int VerifyMigrations(
        IReadOnlyDictionary<string, string> options)
    {
        EnsureOnly(options, "original", "execution", "role");
        var report = MigrationSqlArtifact.Verify(
            Require(options, "original"),
            Require(options, "execution"),
            Require(options, "role"));

        Console.WriteLine(
            $"Migration execution artifact verified: " +
            $"BOM occurrences={report.BomOccurrences}, " +
            $"SET ROLE={report.DatabaseRole}");
        return 0;
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(
        IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();

        if (values.Length % 2 != 0)
        {
            throw new ArgumentException(
                "Every option must have exactly one value.",
                nameof(arguments));
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < values.Length; index += 2)
        {
            var name = values[index];

            if (!name.StartsWith("--", StringComparison.Ordinal) ||
                name.Length == 2)
            {
                throw new ArgumentException(
                    $"Invalid option name '{name}'.",
                    nameof(arguments));
            }

            var key = name[2..];

            if (!options.TryAdd(key, values[index + 1]))
            {
                throw new ArgumentException(
                    $"Option '--{key}' was specified more than once.",
                    nameof(arguments));
            }
        }

        return options;
    }

    private static string Require(
        IReadOnlyDictionary<string, string> options,
        string key)
    {
        if (!options.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Required option '--{key}' is missing.");
        }

        return value;
    }

    private static void EnsureOnly(
        IReadOnlyDictionary<string, string> options,
        params string[] allowedOptions)
    {
        var allowed = allowedOptions.ToHashSet(StringComparer.Ordinal);
        var unknown = options.Keys
            .Where(key => !allowed.Contains(key))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown option '--{unknown}'.");
        }
    }

    private static void WriteReleaseReport(ReleaseArchiveReport report)
    {
        Console.WriteLine(
            $"Release archive verified: directories={report.DirectoryCount} " +
            $"mode={ReleaseArchive.FormatMode(ReleaseArchive.DirectoryMode)}, " +
            $"ordinary files={report.OrdinaryFileCount} " +
            $"mode={ReleaseArchive.FormatMode(ReleaseArchive.OrdinaryFileMode)}, " +
            $"FuaPay.Web mode=" +
            $"{ReleaseArchive.FormatMode(ReleaseArchive.ExecutableFileMode)}");
    }

    private static int WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage:\n" +
            "  release create --source <publish-directory> --archive <tar.gz>\n" +
            "  release verify --archive <tar.gz>\n" +
            "  migrations prepare --input <generated.sql> --output <execution.sql> --role <database-role>\n" +
            "  migrations verify --original <generated.sql> --execution <execution.sql> --role <database-role>");
        return 2;
    }
}
