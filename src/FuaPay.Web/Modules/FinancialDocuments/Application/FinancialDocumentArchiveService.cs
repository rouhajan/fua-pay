using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public sealed partial class FinancialDocumentArchiveService(
    IFinancialDocumentArchiveQueries queries,
    IFinancialDocumentPdfRenderer renderer,
    ILogger<FinancialDocumentArchiveService> logger)
{
    private const int DocumentLimit = 500;
    private const int PdfLimit = 2 * 1024 * 1024;
    private const long ZipLimit = 64L * 1024 * 1024;

    private static readonly SemaphoreSlim ArchiveSlot = new(1, 1);
    private static readonly DateTimeOffset ZipEntryTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal async Task<FinancialDocumentArchiveFile?> PrepareAsync(
        FinancialDocumentArchivePeriod period,
        CancellationToken cancellationToken)
    {
        if (!await ArchiveSlot.WaitAsync(0, cancellationToken))
        {
            throw new FinancialDocumentArchiveException(
                FinancialDocumentArchiveFailure.Busy, "Jiný ZIP export právě probíhá. Zkuste to znovu.");
        }

        var startedAt = Stopwatch.GetTimestamp();
        var documentCount = 0;
        FinancialDocumentArchiveFile? file = null;
        var transferred = false;
        try
        {
            var documents = await queries.ListIssuedAsync(
                period.FromUtc, period.ToUtcExclusive, DocumentLimit + 1, cancellationToken);
            documentCount = documents.Count;
            if (documentCount == 0)
            {
                LogPrepared(logger, 0, 0, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                return null;
            }

            if (documentCount > DocumentLimit)
            {
                throw LimitExceeded();
            }

            file = FinancialDocumentArchiveFile.Create(ArchiveSlot, logger);
            var names = new HashSet<string>(StringComparer.Ordinal);
            using (var bounded = new BoundedZipStream(file.Content, ZipLimit, cancellationToken))
            using (var zip = new ZipArchive(bounded, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var document in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var number = document.DocumentNumber;
                    if (!DocumentNumberPattern().IsMatch(number) || !names.Add(number))
                    {
                        throw new FinancialDocumentArchiveException(
                            FinancialDocumentArchiveFailure.Unavailable,
                            "Doklad má neplatné nebo duplicitní číslo. Archiv nebyl vytvořen.");
                    }

                    var name = $"doklad-o-uhrade-{number}.pdf";
                    FinancialDocumentPdfFile pdf;
                    try
                    {
                        pdf = renderer.Render(document);
                    }
                    catch (FinancialDocumentRenderUnavailableException exception)
                    {
                        var reason = exception.Reason == FinancialDocumentRenderUnavailableReason.LegacyIncompleteSnapshot
                            ? "Historický doklad nemá úplný podklad pro PDF."
                            : "Doklad nelze vykreslit do PDF.";
                        throw new FinancialDocumentArchiveException(
                            FinancialDocumentArchiveFailure.Unavailable,
                            $"{number}: {reason} Archiv nebyl vytvořen.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (pdf.Content.Length > PdfLimit)
                    {
                        throw LimitExceeded();
                    }

                    if (!string.Equals(name, pdf.FileName, StringComparison.Ordinal) ||
                        !pdf.Content.AsSpan().StartsWith("%PDF-"u8))
                    {
                        throw new FinancialDocumentArchiveException(
                            FinancialDocumentArchiveFailure.Unavailable,
                            $"{number}: Renderer nevrátil očekávaný PDF soubor. Archiv nebyl vytvořen.");
                    }

                    var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                    entry.LastWriteTime = ZipEntryTimestamp;
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(pdf.Content, cancellationToken);
                }
            }

            await file.Content.FlushAsync(cancellationToken);
            file.Content.Position = 0;
            LogPrepared(logger, documentCount, file.Content.Length,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            transferred = true;
            return file;
        }
        catch (FinancialDocumentArchiveException exception)
        {
            LogFailed(logger, exception.Failure, documentCount);
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception)
        {
            LogFailed(logger, FinancialDocumentArchiveFailure.Failed, documentCount);
            throw new FinancialDocumentArchiveException(
                FinancialDocumentArchiveFailure.Failed,
                "Při přípravě archivu nastala chyba. Archiv nebyl vytvořen.");
        }
        finally
        {
            if (!transferred)
            {
                if (file is null)
                {
                    ArchiveSlot.Release();
                }
                else
                {
                    file.Dispose();
                }
            }
        }
    }

    private static FinancialDocumentArchiveException LimitExceeded() =>
        new(FinancialDocumentArchiveFailure.Limit,
            "Export překročil limit počtu nebo velikosti souborů. Zúžte období.");

    [GeneratedRegex(@"\AFUA-[0-9]{4}-[0-9]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentNumberPattern();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Financial document ZIP prepared. Documents: {DocumentCount}, bytes: {Bytes}, elapsed ms: {ElapsedMs}.")]
    private static partial void LogPrepared(
        ILogger logger, int documentCount, long bytes, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Financial document ZIP failed. Reason: {Reason}, selected documents: {DocumentCount}.")]
    private static partial void LogFailed(
        ILogger logger, FinancialDocumentArchiveFailure reason, int documentCount);

    internal sealed class BoundedZipStream(
        Stream output, long limit, CancellationToken cancellationToken) : Stream
    {
        private long _bytes;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => output.Flush();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckWrite(buffer.Length);
            output.Write(buffer);
            _bytes += buffer.Length;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            CheckWrite(buffer.Length);
            await output.WriteAsync(buffer, token);
            _bytes += buffer.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        private void CheckWrite(int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count > limit - _bytes)
            {
                throw LimitExceeded();
            }
        }
    }
}

internal enum FinancialDocumentArchiveFailure
{
    Busy,
    Limit,
    Unavailable,
    Failed
}

internal sealed class FinancialDocumentArchiveException(
    FinancialDocumentArchiveFailure failure, string message) : Exception(message)
{
    public FinancialDocumentArchiveFailure Failure { get; } = failure;
}
