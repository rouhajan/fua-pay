using System.Text;

using FuaPay.DeploymentArtifacts;
using FuaPay.Web.Tests.Testing;

namespace FuaPay.Web.Tests.DeploymentArtifacts;

public sealed class MigrationSqlArtifactTests
{
    private static readonly byte[] Bom = [0xef, 0xbb, 0xbf];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepare_PublishesExactBomFreeDerivative(bool includeLeadingBom)
    {
        using var temporary = new TemporaryDirectory("fuapay-migration-sql");
        var originalPath = Path.Combine(temporary.Path, "generated.sql");
        var executionPath = Path.Combine(temporary.Path, "execution.sql");
        var sql = Encoding.UTF8.GetBytes(
            "START TRANSACTION;\r\nSELECT 1;\r\nCOMMIT;\r\n");
        var original = includeLeadingBom
            ? Bom.Concat(sql).ToArray()
            : sql;
        File.WriteAllBytes(originalPath, original);

        var result = MigrationSqlArtifact.Prepare(
            originalPath,
            executionPath,
            "fuapay_migrator");

        Assert.Equal(includeLeadingBom, result.HadLeadingBom);
        Assert.Equal(original, File.ReadAllBytes(originalPath));
        var prelude = MigrationSqlArtifact.CreatePrelude("fuapay_migrator");
        Assert.Equal(
            prelude.Concat(sql).ToArray(),
            File.ReadAllBytes(executionPath));
        Assert.Empty(FindPartials(temporary.Path));

        var verification = MigrationSqlArtifact.Verify(
            originalPath,
            executionPath,
            "fuapay_migrator");
        Assert.Equal(0, verification.BomOccurrences);
    }

    [Fact]
    public void Prepare_RejectsEmbeddedBomWithoutPublishingFinalArtifact()
    {
        using var temporary = new TemporaryDirectory("fuapay-embedded-bom");
        var originalPath = Path.Combine(temporary.Path, "generated.sql");
        var executionPath = Path.Combine(temporary.Path, "execution.sql");
        var prefix = Encoding.UTF8.GetBytes("SELECT 1;\n");
        var suffix = Encoding.UTF8.GetBytes("SELECT 2;\n");
        var original = prefix.Concat(Bom).Concat(suffix).ToArray();
        File.WriteAllBytes(originalPath, original);

        var exception = Assert.Throws<InvalidDataException>(
            () => MigrationSqlArtifact.Prepare(
                originalPath,
                executionPath,
                "fuapay_migrator"));

        Assert.Contains("embedded UTF-8 BOM", exception.Message);
        Assert.Equal(original, File.ReadAllBytes(originalPath));
        Assert.False(File.Exists(executionPath));
        Assert.Empty(FindPartials(temporary.Path));
    }

    [Fact]
    public void Verify_RejectsValidButTruncatedExecutionSql()
    {
        using var temporary = new TemporaryDirectory("fuapay-truncated-sql");
        var paths = PrepareArtifact(temporary.Path);
        var execution = File.ReadAllBytes(paths.Execution);
        File.WriteAllBytes(paths.Execution, execution[..^9]);

        var exception = Assert.Throws<InvalidDataException>(
            () => MigrationSqlArtifact.Verify(
                paths.Original,
                paths.Execution,
                "fuapay_migrator"));

        Assert.Contains("exact expected", exception.Message);
    }

    [Fact]
    public void Verify_RejectsModifiedExecutionRemainder()
    {
        using var temporary = new TemporaryDirectory("fuapay-modified-sql");
        var paths = PrepareArtifact(temporary.Path);
        var execution = File.ReadAllBytes(paths.Execution);
        execution[^2] = execution[^2] == (byte)'1' ? (byte)'2' : (byte)'1';
        File.WriteAllBytes(paths.Execution, execution);

        var exception = Assert.Throws<InvalidDataException>(
            () => MigrationSqlArtifact.Verify(
                paths.Original,
                paths.Execution,
                "fuapay_migrator"));

        Assert.Contains("exact expected", exception.Message);
    }

    [Fact]
    public void Prepare_DoesNotOverwriteExistingFinalArtifact()
    {
        using var temporary = new TemporaryDirectory("fuapay-existing-sql");
        var originalPath = Path.Combine(temporary.Path, "generated.sql");
        var executionPath = Path.Combine(temporary.Path, "execution.sql");
        File.WriteAllText(originalPath, "SELECT 1;\n", new UTF8Encoding(false));
        var sentinel = new byte[] { 4, 5, 6 };
        File.WriteAllBytes(executionPath, sentinel);

        Assert.Throws<IOException>(
            () => MigrationSqlArtifact.Prepare(
                originalPath,
                executionPath,
                "fuapay_migrator"));

        Assert.Equal(sentinel, File.ReadAllBytes(executionPath));
        Assert.Empty(FindPartials(temporary.Path));
    }

    [Theory]
    [InlineData("fuapay\"migrator")]
    [InlineData("fuapay;migrator")]
    [InlineData("fuapay--migrator")]
    [InlineData("fuapay/*migrator")]
    [InlineData(" fuapay_migrator")]
    [InlineData("fuapay_migrator ")]
    [InlineData("fuapay migrator")]
    [InlineData("FUAPAY_MIGRATOR")]
    [InlineData("fuapay_migrátor")]
    [InlineData("fuapay_аpp")]
    public void CreatePrelude_RejectsUnsafeRoleNames(string role)
    {
        Assert.Throws<ArgumentException>(
            () => MigrationSqlArtifact.CreatePrelude(role));
    }

    [Fact]
    public void CreatePrelude_RejectsOverlengthRoleName()
    {
        Assert.Throws<ArgumentException>(
            () => MigrationSqlArtifact.CreatePrelude(
                "a" + new string('b', 63)));
    }

    [Fact]
    public void Cli_RejectsUnknownOptions()
    {
        var exitCode = FuaPay.DeploymentArtifacts.Program.Main(
            [
                "release",
                "verify",
                "--archive",
                "unused.tar.gz",
                "--unexpected",
                "value"
            ]);

        Assert.Equal(1, exitCode);
    }

    private static ArtifactPaths PrepareArtifact(string directory)
    {
        var originalPath = Path.Combine(directory, "generated.sql");
        var executionPath = Path.Combine(directory, "execution.sql");
        File.WriteAllText(
            originalPath,
            "START TRANSACTION;\nSELECT 1;\nCOMMIT;\n",
            new UTF8Encoding(false));
        MigrationSqlArtifact.Prepare(
            originalPath,
            executionPath,
            "fuapay_migrator");
        return new ArtifactPaths(originalPath, executionPath);
    }

    private static string[] FindPartials(string directory) =>
        Directory.GetFiles(
            directory,
            "*.partial",
            SearchOption.AllDirectories);

    private sealed record ArtifactPaths(string Original, string Execution);
}
