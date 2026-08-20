using System.Text;

namespace FuaPay.Web.Tests.Architecture;

public sealed class ModuleBoundaryTests
{
    [Fact]
    public void ServiceType_PreservesPersistedNumericValues()
    {
        Assert.Equal(0, (int)ServiceType.Unknown);
        Assert.Equal(1, (int)ServiceType.ThreeDPrint);
        Assert.Equal(2, (int)ServiceType.LargeFormatPrint);
        Assert.Equal(3, (int)ServiceType.Workshop);
        Assert.Equal(4, (int)ServiceType.Other);
    }

    [Theory]
    [InlineData("Access")]
    [InlineData("Credits")]
    [InlineData("Jobs")]
    [InlineData("Payments")]
    [InlineData("ServiceUnits")]
    public void ReceiptSourceModules_DoNotDependOnReceiptsModule(
        string moduleName)
    {
        var moduleDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "FuaPay.Web",
            "Modules",
            moduleName);
        var violations = Directory
            .EnumerateFiles(
                moduleDirectory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Where(path => File.ReadAllText(path, Encoding.UTF8).Contains(
                "FuaPay.Web.Modules.Receipts",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(moduleDirectory, path))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ServiceUnits_DoesNotDependOnJobsModule()
    {
        var sourceDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "FuaPay.Web",
            "Modules",
            "ServiceUnits");
        var violations = Directory
            .EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Where(path => File.ReadAllText(path, Encoding.UTF8).Contains(
                "FuaPay.Web.Modules.Jobs",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceDirectory, path))
            .ToArray();

        Assert.Empty(violations);
    }

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

    private static bool IsGeneratedPath(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);
}
