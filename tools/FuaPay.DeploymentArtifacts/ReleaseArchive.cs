using System.ComponentModel;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace FuaPay.DeploymentArtifacts;

public sealed record ReleaseArchiveReport(
    int DirectoryCount,
    int OrdinaryFileCount);

public static class ReleaseArchive
{
    public const string ExecutableName = "FuaPay.Web";

    public const UnixFileMode DirectoryMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute |
        UnixFileMode.GroupRead |
        UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute;

    public const UnixFileMode OrdinaryFileMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.GroupRead |
        UnixFileMode.GroupWrite;

    public const UnixFileMode ExecutableFileMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute |
        UnixFileMode.GroupRead |
        UnixFileMode.GroupExecute;

    private const int GzipHeaderLength = 10;
    private const int GzipExtraFlagsOffset = 8;
    private const int LinuxCurrentWorkingDirectory = -100;
    private const int LinuxNoFollow = 0x100;
    private const uint LinuxStatxType = 0x0001;
    private const int LinuxStatxBufferLength = 256;
    private const int LinuxStatxModeOffset = 28;
    private const ushort LinuxFileTypeMask = 0xf000;
    private const ushort LinuxRegularFile = 0x8000;
    private const ushort LinuxDirectory = 0x4000;
    private const ushort LinuxSymbolicLink = 0xa000;
    private const ushort LinuxFifo = 0x1000;
    private const ushort LinuxSocket = 0xc000;
    private const ushort LinuxCharacterDevice = 0x2000;
    private const ushort LinuxBlockDevice = 0x6000;
    private const int UstarNameByteLimit = 100;
    private const int UstarPrefixByteLimit = 155;
    private const long UstarFileSizeLimit = 8_589_934_591;

    private static readonly DateTimeOffset DeterministicTimestamp =
        DateTimeOffset.UnixEpoch;

    private static readonly byte[] DeterministicGzipHeader =
        [0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff];

    public static ReleaseArchiveReport Create(
        string publishDirectory,
        string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        var sourcePath = Path.GetFullPath(publishDirectory);
        var outputPath = Path.GetFullPath(archivePath);

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Publish directory '{sourcePath}' does not exist.");
        }

        if (IsWithin(outputPath, sourcePath))
        {
            throw new ArgumentException(
                "The release archive must be outside the publish directory.",
                nameof(archivePath));
        }

        if (File.Exists(outputPath))
        {
            throw new IOException(
                $"Release archive '{outputPath}' already exists.");
        }

        var entries = EnumerateAndValidateEntries(sourcePath);

        if (!entries.Any(
                entry =>
                    !entry.IsDirectory &&
                    string.Equals(
                        entry.ArchiveName,
                        ExecutableName,
                        StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Publish directory does not contain '{ExecutableName}'.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new ArgumentException(
                "The release archive path has no parent directory.",
                nameof(archivePath));
        Directory.CreateDirectory(outputDirectory);

        var partialPath = CreatePartialPath(outputPath);

        try
        {
            WriteArchive(entries, partialPath);
            var report = Verify(partialPath);
            File.Move(partialPath, outputPath);
            return report;
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    public static ReleaseArchiveReport Verify(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var fullPath = Path.GetFullPath(archivePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Release archive does not exist.",
                fullPath);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var directoryCount = 0;
        var ordinaryFileCount = 0;
        var executableCount = 0;
        var rootDirectorySeen = false;

        using var archiveStream = File.OpenRead(fullPath);
        VerifyGzipHeader(archiveStream);
        archiveStream.Position = 0;

        using var gzipStream = new GZipStream(
            archiveStream,
            CompressionMode.Decompress,
            leaveOpen: false);
        using var reader = new TarReader(gzipStream, leaveOpen: false);

        while (reader.GetNextEntry(copyData: false) is { } entry)
        {
            var normalizedName = NormalizeArchiveName(entry.Name);

            if (!names.Add(normalizedName))
            {
                throw new InvalidDataException(
                    $"Release archive contains duplicate entry '{entry.Name}'.");
            }

            EnsureDeterministicMetadata(entry);

            if (entry.EntryType == TarEntryType.Directory)
            {
                EnsureMode(entry, DirectoryMode);
                directoryCount++;
                rootDirectorySeen |= normalizedName == ".";
                continue;
            }

            if (entry.EntryType is not
                TarEntryType.RegularFile and not
                TarEntryType.V7RegularFile)
            {
                throw new InvalidDataException(
                    $"Release archive entry '{entry.Name}' has unsupported " +
                    $"type '{entry.EntryType}'.");
            }

            if (string.Equals(
                    normalizedName,
                    ExecutableName,
                    StringComparison.Ordinal))
            {
                EnsureMode(entry, ExecutableFileMode);
                executableCount++;
            }
            else
            {
                EnsureMode(entry, OrdinaryFileMode);
                ordinaryFileCount++;
            }
        }

        if (!rootDirectorySeen)
        {
            throw new InvalidDataException(
                "Release archive does not contain its root directory entry.");
        }

        if (executableCount != 1)
        {
            throw new InvalidDataException(
                $"Release archive must contain exactly one '{ExecutableName}' entry.");
        }

        return new ReleaseArchiveReport(
            directoryCount,
            ordinaryFileCount);
    }

    public static string FormatMode(UnixFileMode mode) =>
        Convert.ToString((int)mode, 8).PadLeft(4, '0');

    private static IReadOnlyList<SourceEntry> EnumerateAndValidateEntries(
        string sourcePath)
    {
        if (GetFileSystemEntryType(sourcePath) != FileSystemEntryType.Directory)
        {
            throw new InvalidDataException(
                $"Publish source '{sourcePath}' is not a regular directory.");
        }

        EnsureUstarPathRepresentable(".");

        var entries = new List<SourceEntry>();
        var pending = new Stack<string>();
        pending.Push(sourcePath);

        while (pending.Count != 0)
        {
            var directory = pending.Pop();

            foreach (var path in Directory
                .EnumerateFileSystemEntries(directory)
                .Order(StringComparer.Ordinal))
            {
                var type = GetFileSystemEntryType(path);
                var archiveName = Path
                    .GetRelativePath(sourcePath, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                EnsureUstarPathRepresentable(archiveName);

                if (type == FileSystemEntryType.Directory)
                {
                    entries.Add(new SourceEntry(path, archiveName, true));
                    pending.Push(path);
                    continue;
                }

                if (type != FileSystemEntryType.RegularFile)
                {
                    throw new InvalidDataException(
                        $"Publish entry '{path}' is an unsupported " +
                        $"{Describe(type)}.");
                }

                var length = new FileInfo(path).Length;

                if (length > UstarFileSizeLimit)
                {
                    throw new InvalidDataException(
                        $"Publish entry '{path}' is too large for USTAR.");
                }

                entries.Add(new SourceEntry(path, archiveName, false));
            }
        }

        return entries
            .OrderBy(entry => entry.ArchiveName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteArchive(
        IReadOnlyList<SourceEntry> entries,
        string partialPath)
    {
        using var archiveStream = new FileStream(
            partialPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);

        using (var gzipStream = new GZipStream(
                   archiveStream,
                   CompressionLevel.NoCompression,
                   leaveOpen: true))
        {
            using var writer = new TarWriter(
                gzipStream,
                TarEntryFormat.Ustar,
                leaveOpen: true);

            WriteDirectory(writer, ".");

            foreach (var entry in entries.Where(item => item.IsDirectory))
            {
                WriteDirectory(writer, entry.ArchiveName);
            }

            foreach (var entry in entries.Where(item => !item.IsDirectory))
            {
                WriteFile(writer, entry);
            }
        }

        NormalizeGzipHeader(archiveStream);
        archiveStream.Flush(flushToDisk: true);
    }

    private static void WriteDirectory(TarWriter writer, string name)
    {
        var entry = CreateEntry(TarEntryType.Directory, name, DirectoryMode);
        writer.WriteEntry(entry);
    }

    private static void WriteFile(TarWriter writer, SourceEntry source)
    {
        var mode = string.Equals(
                source.ArchiveName,
                ExecutableName,
                StringComparison.Ordinal)
            ? ExecutableFileMode
            : OrdinaryFileMode;
        var entry = CreateEntry(
            TarEntryType.RegularFile,
            source.ArchiveName,
            mode);

        using var data = File.OpenRead(source.FullPath);
        entry.DataStream = data;
        writer.WriteEntry(entry);
    }

    private static UstarTarEntry CreateEntry(
        TarEntryType type,
        string name,
        UnixFileMode mode)
    {
        return new UstarTarEntry(type, name)
        {
            Mode = mode,
            Uid = 0,
            Gid = 0,
            UserName = string.Empty,
            GroupName = string.Empty,
            ModificationTime = DeterministicTimestamp
        };
    }

    private static void NormalizeGzipHeader(FileStream archiveStream)
    {
        Span<byte> header = stackalloc byte[GzipHeaderLength];
        archiveStream.Position = 0;
        archiveStream.ReadExactly(header);

        if (!header[..GzipExtraFlagsOffset]
            .SequenceEqual(
                DeterministicGzipHeader.AsSpan()[..GzipExtraFlagsOffset]))
        {
            throw new InvalidDataException(
                "GZipStream emitted an unexpected gzip header.");
        }

        archiveStream.Position = GzipExtraFlagsOffset;
        archiveStream.Write(
            DeterministicGzipHeader.AsSpan()[GzipExtraFlagsOffset..]);
    }

    private static void VerifyGzipHeader(Stream archiveStream)
    {
        Span<byte> header = stackalloc byte[GzipHeaderLength];

        try
        {
            archiveStream.ReadExactly(header);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException(
                "Release archive has an incomplete gzip header.",
                exception);
        }

        if (!header.SequenceEqual(DeterministicGzipHeader))
        {
            throw new InvalidDataException(
                "Release archive has non-deterministic gzip metadata.");
        }
    }

    private static void EnsureMode(TarEntry entry, UnixFileMode expected)
    {
        if (entry.Mode != expected)
        {
            throw new InvalidDataException(
                $"Release archive entry '{entry.Name}' has mode " +
                $"{FormatMode(entry.Mode)}; expected {FormatMode(expected)}.");
        }
    }

    private static void EnsureDeterministicMetadata(TarEntry entry)
    {
        if (entry is not PosixTarEntry posixEntry ||
            entry.Format != TarEntryFormat.Ustar ||
            entry.ModificationTime != DeterministicTimestamp ||
            entry.Uid != 0 ||
            entry.Gid != 0 ||
            posixEntry.UserName != string.Empty ||
            posixEntry.GroupName != string.Empty)
        {
            throw new InvalidDataException(
                $"Release archive entry '{entry.Name}' has " +
                "non-deterministic metadata.");
        }
    }

    private static void EnsureUstarPathRepresentable(string archiveName)
    {
        var encoding = Encoding.UTF8;

        if (encoding.GetByteCount(archiveName) <= UstarNameByteLimit)
        {
            return;
        }

        for (var index = archiveName.LastIndexOf('/');
             index > 0;
             index = archiveName.LastIndexOf('/', index - 1))
        {
            var prefix = archiveName.AsSpan(0, index);
            var name = archiveName.AsSpan(index + 1);

            if (name.Length != 0 &&
                encoding.GetByteCount(prefix) <= UstarPrefixByteLimit &&
                encoding.GetByteCount(name) <= UstarNameByteLimit)
            {
                return;
            }
        }

        throw new InvalidDataException(
            $"Archive path '{archiveName}' cannot be represented in USTAR.");
    }

    private static string NormalizeArchiveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\'))
        {
            throw new InvalidDataException(
                $"Release archive entry name '{name}' is invalid.");
        }

        var normalized = name.EndsWith("/", StringComparison.Ordinal)
            ? name[..^1]
            : name;

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length == 0)
        {
            normalized = ".";
        }

        if (Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(part => part == ".."))
        {
            throw new InvalidDataException(
                $"Release archive entry name '{name}' escapes the archive root.");
        }

        return normalized;
    }

    private static FileSystemEntryType GetFileSystemEntryType(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(path);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return FileSystemEntryType.SymbolicLinkOrReparsePoint;
            }

            return (attributes & FileAttributes.Directory) != 0
                ? FileSystemEntryType.Directory
                : FileSystemEntryType.RegularFile;
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "Release packaging supports Windows and Linux.");
        }

        var buffer = Marshal.AllocHGlobal(LinuxStatxBufferLength);

        try
        {
            if (Statx(
                    LinuxCurrentWorkingDirectory,
                    path,
                    LinuxNoFollow,
                    LinuxStatxType,
                    buffer) != 0)
            {
                var error = new Win32Exception(Marshal.GetLastPInvokeError());
                throw new IOException(
                    $"Could not inspect publish entry '{path}': {error.Message}",
                    error);
            }

            var mode = unchecked(
                (ushort)Marshal.ReadInt16(buffer, LinuxStatxModeOffset));

            return (ushort)(mode & LinuxFileTypeMask) switch
            {
                LinuxRegularFile => FileSystemEntryType.RegularFile,
                LinuxDirectory => FileSystemEntryType.Directory,
                LinuxSymbolicLink =>
                    FileSystemEntryType.SymbolicLinkOrReparsePoint,
                LinuxFifo => FileSystemEntryType.Fifo,
                LinuxSocket => FileSystemEntryType.Socket,
                LinuxCharacterDevice => FileSystemEntryType.CharacterDevice,
                LinuxBlockDevice => FileSystemEntryType.BlockDevice,
                _ => FileSystemEntryType.Other
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Describe(FileSystemEntryType type) =>
        type switch
        {
            FileSystemEntryType.SymbolicLinkOrReparsePoint =>
                "symbolic link or reparse point",
            FileSystemEntryType.Fifo => "FIFO",
            FileSystemEntryType.Socket => "socket",
            FileSystemEntryType.CharacterDevice => "character device",
            FileSystemEntryType.BlockDevice => "block device",
            _ => "special filesystem object"
        };

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

    private static bool IsWithin(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);

        return !Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(
        int directoryFileDescriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags,
        uint mask,
        IntPtr buffer);

    private enum FileSystemEntryType
    {
        RegularFile,
        Directory,
        SymbolicLinkOrReparsePoint,
        Fifo,
        Socket,
        CharacterDevice,
        BlockDevice,
        Other
    }

    private sealed record SourceEntry(
        string FullPath,
        string ArchiveName,
        bool IsDirectory);
}
