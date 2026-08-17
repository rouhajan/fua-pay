namespace FuaPay.Web.Tests.Testing;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
