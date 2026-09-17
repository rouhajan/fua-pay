using System.Text;

namespace FuaPay.Web.Tests.Modules.FinancialDocuments.Application;

public sealed class FinancialDocumentMigrationContractTests
{
    [Fact]
    public void TaxSnapshotMigration_IsAdditiveAndContainsNoBackfill()
    {
        var root = FindRepositoryRoot();
        var migrationPath = Directory.GetFiles(
            Path.Combine(
                root,
                "src",
                "FuaPay.Web",
                "BuildingBlocks",
                "Persistence",
                "Migrations"),
            "*AddFinancialDocumentTaxSnapshot.cs",
            SearchOption.TopDirectoryOnly).Single();
        var source = File.ReadAllText(migrationPath, Encoding.UTF8);

        Assert.Equal(4, Count(source, "nullable: true"));
        Assert.DoesNotContain("defaultValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UpdateData", source, StringComparison.Ordinal);
        Assert.Contains("amount_minor_units::numeric * 10000 / 12100", source);
        Assert.Contains(
            "vat_amount_minor_units = amount_minor_units - tax_base_minor_units",
            source);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FuaPay.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FuaPay.slnx was not found.");
    }
}
