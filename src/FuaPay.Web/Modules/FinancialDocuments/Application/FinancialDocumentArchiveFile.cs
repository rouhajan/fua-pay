using System.Security.AccessControl;
using System.Security.Principal;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

internal sealed partial class FinancialDocumentArchiveFile : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly SemaphoreSlim _slot;
    private readonly ILogger _logger;
    private int _disposed;

    private FinancialDocumentArchiveFile(
        DirectoryInfo directory, FileStream content, SemaphoreSlim slot, ILogger logger)
    {
        _directory = directory;
        Content = content;
        _slot = slot;
        _logger = logger;
    }

    public FileStream Content { get; }

    internal static string DirectoryPrefix => $"fuapay-documents-{Environment.ProcessId}-";

    public static FinancialDocumentArchiveFile Create(SemaphoreSlim slot, ILogger logger)
    {
        var directory = CreatePrivateDirectory();
        try
        {
            var file = new FileStream(Path.Combine(directory.FullName, "archive.zip"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            return new FinancialDocumentArchiveFile(directory, file, slot, logger);
        }
        catch
        {
            DeleteDirectory(directory, logger);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Content.Dispose();
        }
        finally
        {
            try
            {
                DeleteDirectory(_directory, _logger);
            }
            finally
            {
                _slot.Release();
            }
        }
    }

    private static DirectoryInfo CreatePrivateDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Directory.CreateTempSubdirectory(DirectoryPrefix);
        }

        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("Missing temporary file owner.");
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));

        var directory = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"{DirectoryPrefix}{Guid.NewGuid():N}"));
        directory.Create(security);
        return directory;
    }

    private static void DeleteDirectory(DirectoryInfo directory, ILogger logger)
    {
        try
        {
            directory.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCleanupFailed(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Financial document ZIP temporary directory cleanup failed.")]
    private static partial void LogCleanupFailed(ILogger logger);
}
