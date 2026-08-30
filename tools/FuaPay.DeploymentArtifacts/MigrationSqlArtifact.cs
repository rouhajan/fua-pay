using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FuaPay.DeploymentArtifacts;

public sealed record MigrationSqlPreparationResult(
    bool HadLeadingBom,
    string OriginalSha256,
    string ExecutionSha256);

public sealed record MigrationSqlVerificationReport(
    string DatabaseRole,
    int BomOccurrences);

public static partial class MigrationSqlArtifact
{
    private static readonly byte[] Utf8Bom = [0xef, 0xbb, 0xbf];
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static MigrationSqlPreparationResult Prepare(
        string originalSqlPath,
        string executionSqlPath,
        string databaseRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalSqlPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionSqlPath);
        var normalizedRole = ValidateRole(databaseRole);
        var originalPath = Path.GetFullPath(originalSqlPath);
        var executionPath = Path.GetFullPath(executionSqlPath);

        if (string.Equals(
                originalPath,
                executionPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Original and execution SQL paths must be different.",
                nameof(executionSqlPath));
        }

        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException(
                "Original EF migration SQL does not exist.",
                originalPath);
        }

        if (File.Exists(executionPath))
        {
            throw new IOException(
                $"Migration execution artifact '{executionPath}' already exists.");
        }

        var original = File.ReadAllBytes(originalPath);
        var content = ValidateAndGetContent(original, out var hadLeadingBom);
        var prelude = CreatePrelude(normalizedRole);
        var outputDirectory = Path.GetDirectoryName(executionPath)
            ?? throw new ArgumentException(
                "The execution SQL path has no parent directory.",
                nameof(executionSqlPath));
        Directory.CreateDirectory(outputDirectory);

        var partialPath = CreatePartialPath(executionPath);

        try
        {
            using (var output = new FileStream(
                       partialPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                output.Write(prelude);
                output.Write(content);
                output.Flush(flushToDisk: true);
            }

            Verify(originalPath, partialPath, normalizedRole);

            var unchanged = File.ReadAllBytes(originalPath);

            if (!original.AsSpan().SequenceEqual(unchanged))
            {
                throw new IOException(
                    "Original EF migration SQL changed while preparing the " +
                    "execution artifact.");
            }

            var result = new MigrationSqlPreparationResult(
                hadLeadingBom,
                Hash(original),
                Hash(File.ReadAllBytes(partialPath)));

            File.Move(partialPath, executionPath);
            return result;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public static MigrationSqlVerificationReport Verify(
        string originalSqlPath,
        string executionSqlPath,
        string databaseRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalSqlPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionSqlPath);
        var normalizedRole = ValidateRole(databaseRole);
        var originalPath = Path.GetFullPath(originalSqlPath);
        var executionPath = Path.GetFullPath(executionSqlPath);

        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException(
                "Original EF migration SQL does not exist.",
                originalPath);
        }

        if (!File.Exists(executionPath))
        {
            throw new FileNotFoundException(
                "Migration execution SQL does not exist.",
                executionPath);
        }

        var original = File.ReadAllBytes(originalPath);
        var content = ValidateAndGetContent(original, out _);
        var execution = File.ReadAllBytes(executionPath);
        var prelude = CreatePrelude(normalizedRole);
        var expectedLength = checked(prelude.Length + content.Length);

        if (execution.Length != expectedLength ||
            !execution.AsSpan(0, prelude.Length).SequenceEqual(prelude) ||
            !execution.AsSpan(prelude.Length).SequenceEqual(content))
        {
            throw new InvalidDataException(
                "Migration execution SQL is not the exact expected SET ROLE " +
                "prefix plus the original BOM-free SQL bytes.");
        }

        var bomOccurrences = CountBomOccurrences(execution);

        if (bomOccurrences != 0)
        {
            throw new InvalidDataException(
                "Migration execution SQL contains a UTF-8 BOM.");
        }

        return new MigrationSqlVerificationReport(
            normalizedRole,
            bomOccurrences);
    }

    internal static byte[] CreatePrelude(string databaseRole)
    {
        var normalizedRole = ValidateRole(databaseRole);
        return StrictUtf8.GetBytes($"SET ROLE \"{normalizedRole}\";\n");
    }

    private static ReadOnlySpan<byte> ValidateAndGetContent(
        byte[] original,
        out bool hadLeadingBom)
    {
        hadLeadingBom = original.AsSpan().StartsWith(Utf8Bom);
        var contentOffset = hadLeadingBom ? Utf8Bom.Length : 0;
        var content = original.AsSpan(contentOffset);

        if (content.Length == 0)
        {
            throw new InvalidDataException(
                "Original EF migration SQL contains no SQL content.");
        }

        if (content.IndexOf(Utf8Bom) >= 0)
        {
            throw new InvalidDataException(
                "Original EF migration SQL contains an embedded UTF-8 BOM.");
        }

        try
        {
            _ = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Original EF migration SQL is not valid UTF-8.",
                exception);
        }

        return content;
    }

    private static string ValidateRole(string databaseRole)
    {
        if (string.IsNullOrWhiteSpace(databaseRole))
        {
            throw new ArgumentException(
                "Database role must not be blank.",
                nameof(databaseRole));
        }

        if (!DatabaseRolePattern().IsMatch(databaseRole))
        {
            throw new ArgumentException(
                "Database role must be an exact lowercase PostgreSQL " +
                "identifier containing only ASCII letters, digits and " +
                "underscores.",
                nameof(databaseRole));
        }

        return databaseRole;
    }

    private static int CountBomOccurrences(ReadOnlySpan<byte> bytes)
    {
        var count = 0;
        var offset = 0;

        while (offset <= bytes.Length - Utf8Bom.Length)
        {
            var index = bytes[offset..].IndexOf(Utf8Bom);

            if (index < 0)
            {
                break;
            }

            count++;
            offset += index + Utf8Bom.Length;
        }

        return count;
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string CreatePartialPath(string finalPath) =>
        Path.Combine(
            Path.GetDirectoryName(finalPath)
                ?? throw new ArgumentException(
                    "The final artifact path has no parent directory.",
                    nameof(finalPath)),
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.partial");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseRolePattern();
}
