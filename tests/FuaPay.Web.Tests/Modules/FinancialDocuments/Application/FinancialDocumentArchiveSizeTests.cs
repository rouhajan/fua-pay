using System.IO.Compression;

using FuaPay.Web.Modules.FinancialDocuments.Application;

namespace FuaPay.Web.Tests.Modules.FinancialDocuments.Application;

public sealed class FinancialDocumentArchiveSizeTests
{
    [Fact]
    public void LimitAlsoAppliesWhenZipWritesCentralDirectory()
    {
        using var backing = new MemoryStream();
        using var bounded = new FinancialDocumentArchiveService.BoundedZipStream(
            backing, 120, CancellationToken.None);
        var zip = new ZipArchive(bounded, ZipArchiveMode.Create, leaveOpen: true);
        using (var entry = zip.CreateEntry("test.pdf", CompressionLevel.NoCompression).Open())
        {
            entry.Write("%PDF-1.7 test"u8);
        }

        var error = Assert.Throws<FinancialDocumentArchiveException>(zip.Dispose);
        Assert.Equal(FinancialDocumentArchiveFailure.Limit, error.Failure);
        Assert.InRange(backing.Length, 1, 120);
    }
}
