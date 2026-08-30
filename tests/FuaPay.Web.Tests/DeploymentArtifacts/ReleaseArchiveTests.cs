using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

using FuaPay.DeploymentArtifacts;
using FuaPay.Web.Tests.Testing;

namespace FuaPay.Web.Tests.DeploymentArtifacts;

public sealed class ReleaseArchiveTests
{
    private static readonly byte[] ExpectedGzipHeader =
        [0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff];

    [Fact]
    public void Create_WritesAndVerifiesDeterministicArchive()
    {
        using var temporary = new TemporaryDirectory("fuapay-release-archive");
        var publishDirectory = CreatePublishTree(temporary.Path);
        var firstArchive = Path.Combine(temporary.Path, "first.tar.gz");
        var secondArchive = Path.Combine(temporary.Path, "second.tar.gz");

        var report = ReleaseArchive.Create(publishDirectory, firstArchive);
        ReleaseArchive.Create(publishDirectory, secondArchive);

        Assert.Equal(3, report.DirectoryCount);
        Assert.Equal(2, report.OrdinaryFileCount);
        Assert.Equal(
            File.ReadAllBytes(firstArchive),
            File.ReadAllBytes(secondArchive));
        Assert.Equal(
            ExpectedGzipHeader,
            File.ReadAllBytes(firstArchive)[..ExpectedGzipHeader.Length]);
        Assert.Empty(FindPartials(temporary.Path));

        var entries = ReadEntries(firstArchive);
        Assert.All(
            entries,
            item =>
            {
                Assert.Equal(TarEntryFormat.Ustar, item.Format);
                Assert.Equal(0, item.Uid);
                Assert.Equal(0, item.Gid);
                Assert.Equal(string.Empty, item.UserName);
                Assert.Equal(string.Empty, item.GroupName);
                Assert.Equal(DateTimeOffset.UnixEpoch, item.ModificationTime);
            });
        Assert.All(
            entries.Where(item => item.Type == TarEntryType.Directory),
            item => Assert.Equal(ReleaseArchive.DirectoryMode, item.Mode));
        Assert.Equal(
            ReleaseArchive.ExecutableFileMode,
            entries.Single(
                item => item.Name == ReleaseArchive.ExecutableName).Mode);
        Assert.All(
            entries.Where(
                item => item.Type != TarEntryType.Directory &&
                    item.Name != ReleaseArchive.ExecutableName),
            item => Assert.Equal(ReleaseArchive.OrdinaryFileMode, item.Mode));
    }

    [Fact]
    public void Create_RejectsUnrepresentableUstarPathWithoutFinalArchive()
    {
        using var temporary = new TemporaryDirectory("fuapay-ustar-path");
        var publishDirectory = CreatePublishTree(temporary.Path);
        var longName = new string('a', 101);
        File.WriteAllText(Path.Combine(publishDirectory, longName), "too long");
        var archivePath = Path.Combine(temporary.Path, "release.tar.gz");

        var exception = Assert.Throws<InvalidDataException>(
            () => ReleaseArchive.Create(publishDirectory, archivePath));

        Assert.Contains("cannot be represented in USTAR", exception.Message);
        Assert.False(File.Exists(archivePath));
        Assert.Empty(FindPartials(temporary.Path));
    }

    [Fact]
    public void Create_DoesNotOverwriteExistingFinalArchive()
    {
        using var temporary = new TemporaryDirectory("fuapay-existing-archive");
        var publishDirectory = CreatePublishTree(temporary.Path);
        var archivePath = Path.Combine(temporary.Path, "release.tar.gz");
        var sentinel = new byte[] { 7, 8, 9 };
        File.WriteAllBytes(archivePath, sentinel);

        Assert.Throws<IOException>(
            () => ReleaseArchive.Create(publishDirectory, archivePath));

        Assert.Equal(sentinel, File.ReadAllBytes(archivePath));
        Assert.Empty(FindPartials(temporary.Path));
    }

    [Fact]
    public void Create_RejectsFifoWithoutFinalArchiveOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var temporary = new TemporaryDirectory("fuapay-fifo-archive");
        var publishDirectory = CreatePublishTree(temporary.Path);
        var fifoPath = Path.Combine(publishDirectory, "unexpected.pipe");

        if (MkFifo(fifoPath, Convert.ToUInt32("660", 8)) != 0)
        {
            throw new IOException(
                $"mkfifo failed with errno {Marshal.GetLastPInvokeError()}.");
        }

        var archivePath = Path.Combine(temporary.Path, "release.tar.gz");

        var exception = Assert.Throws<InvalidDataException>(
            () => ReleaseArchive.Create(publishDirectory, archivePath));

        Assert.Contains("unsupported FIFO", exception.Message);
        Assert.False(File.Exists(archivePath));
        Assert.Empty(FindPartials(temporary.Path));
    }

    [Fact]
    public void Verify_RejectsArchiveWithNonExecutableHost()
    {
        using var temporary = new TemporaryDirectory("fuapay-release-mode");
        var archivePath = Path.Combine(temporary.Path, "invalid.tar.gz");

        WriteCustomArchive(
            archivePath,
            userName: string.Empty,
            groupName: string.Empty,
            executableMode: ReleaseArchive.OrdinaryFileMode);

        var exception = Assert.Throws<InvalidDataException>(
            () => ReleaseArchive.Verify(archivePath));

        Assert.Contains("expected 0750", exception.Message);
    }

    [Fact]
    public void Verify_RejectsUnexpectedUstarOwnerNames()
    {
        using var temporary = new TemporaryDirectory("fuapay-release-owner");
        var archivePath = Path.Combine(temporary.Path, "invalid.tar.gz");

        WriteCustomArchive(
            archivePath,
            userName: "root",
            groupName: "root",
            executableMode: ReleaseArchive.ExecutableFileMode);

        var exception = Assert.Throws<InvalidDataException>(
            () => ReleaseArchive.Verify(archivePath));

        Assert.Contains("non-deterministic metadata", exception.Message);
    }

    [Fact]
    public void Verify_RejectsUnexpectedGzipMetadata()
    {
        using var temporary = new TemporaryDirectory("fuapay-gzip-header");
        var publishDirectory = CreatePublishTree(temporary.Path);
        var archivePath = Path.Combine(temporary.Path, "release.tar.gz");
        ReleaseArchive.Create(publishDirectory, archivePath);
        var bytes = File.ReadAllBytes(archivePath);
        bytes[4] = 1;
        File.WriteAllBytes(archivePath, bytes);

        var exception = Assert.Throws<InvalidDataException>(
            () => ReleaseArchive.Verify(archivePath));

        Assert.Contains("non-deterministic gzip metadata", exception.Message);
    }

    private static string CreatePublishTree(string root)
    {
        var publishDirectory = Path.Combine(root, "publish");
        var nestedDirectory = Path.Combine(publishDirectory, "wwwroot", "css");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(
            Path.Combine(publishDirectory, ReleaseArchive.ExecutableName),
            "executable");
        File.WriteAllText(
            Path.Combine(publishDirectory, "FuaPay.Web.dll"),
            "assembly");
        File.WriteAllText(
            Path.Combine(nestedDirectory, "site.css"),
            "body {}");
        return publishDirectory;
    }

    private static string[] FindPartials(string directory) =>
        Directory.GetFiles(
            directory,
            "*.partial",
            SearchOption.AllDirectories);

    private static IReadOnlyList<ArchiveEntry> ReadEntries(string archivePath)
    {
        var entries = new List<ArchiveEntry>();
        using var file = File.OpenRead(archivePath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);

        while (reader.GetNextEntry(copyData: false) is { } entry)
        {
            var posixEntry = Assert.IsAssignableFrom<PosixTarEntry>(entry);
            entries.Add(new ArchiveEntry(
                entry.Name.TrimEnd('/'),
                entry.EntryType,
                entry.Mode,
                entry.Format,
                entry.Uid,
                entry.Gid,
                posixEntry.UserName,
                posixEntry.GroupName,
                entry.ModificationTime));
        }

        return entries;
    }

    private static void WriteCustomArchive(
        string archivePath,
        string userName,
        string groupName,
        UnixFileMode executableMode)
    {
        using (var file = File.Create(archivePath))
        using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Ustar))
        {
            writer.WriteEntry(CreateEntry(
                TarEntryType.Directory,
                ".",
                ReleaseArchive.DirectoryMode,
                userName,
                groupName));
            writer.WriteEntry(CreateEntry(
                TarEntryType.RegularFile,
                ReleaseArchive.ExecutableName,
                executableMode,
                userName,
                groupName));
        }

        var bytes = File.ReadAllBytes(archivePath);
        ExpectedGzipHeader.CopyTo(bytes, 0);
        File.WriteAllBytes(archivePath, bytes);
    }

    private static UstarTarEntry CreateEntry(
        TarEntryType type,
        string name,
        UnixFileMode mode,
        string userName,
        string groupName)
    {
        var entry = new UstarTarEntry(type, name)
        {
            Mode = mode,
            Uid = 0,
            Gid = 0,
            UserName = userName,
            GroupName = groupName,
            ModificationTime = DateTimeOffset.UnixEpoch
        };

        if (type == TarEntryType.RegularFile)
        {
            entry.DataStream = new MemoryStream([1, 2, 3]);
        }

        return entry;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        uint mode);

    private sealed record ArchiveEntry(
        string Name,
        TarEntryType Type,
        UnixFileMode Mode,
        TarEntryFormat Format,
        int Uid,
        int Gid,
        string UserName,
        string GroupName,
        DateTimeOffset ModificationTime);
}
