using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.RegularExpressions;

using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Modules.Reporting.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Tests.Hosting;

public sealed class FinancialDocumentArchiveHttpTests(ConfiguredWebApplicationFactory factory)
    : IClassFixture<ConfiguredWebApplicationFactory>
{
    private const string Exports = "/Admin/Exports";
    private const string Archive = Exports + "?handler=FinancialDocuments";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task AdminCanDownloadZipAndExistingPdfAndCsvContractsStillWork()
    {
        var fixture = new TestData(AccessRole.Admin);
        var completion = new ResponseGate();
        using var app = CreateApp(fixture, completion);
        using var client = Client(app, fixture);
        using var directGet = await client.GetAsync(Archive);
        Assert.Equal(HttpStatusCode.OK, directGet.StatusCode);
        Assert.Equal(0, fixture.ArchiveReads);
        var token = await AntiforgeryTokenAsync(client);
        var before = TemporaryDirectories();

        using (var response = await PostObservedArchiveAsync(client, token, completion))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("doklady-o-uhrade-2026-03-01--2026-03-31.zip",
                response.Content.Headers.ContentDisposition?.FileNameStar);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.CacheControl?.Private);
            using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
            var entry = Assert.Single(archive.Entries);
            Assert.Equal("doklad-o-uhrade-FUA-2026-000001.pdf", entry.FullName);
            using var content = new MemoryStream();
            await using var source = entry.Open();
            await source.CopyToAsync(content);
            Assert.Equal(TestData.Pdf, content.ToArray());
        }

        Assert.Equal(1, fixture.ArchiveReads);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 23, 0, 0, TimeSpan.Zero), fixture.ArchiveFrom);
        Assert.Equal(new DateTimeOffset(2026, 3, 31, 22, 0, 0, TimeSpan.Zero), fixture.ArchiveTo);
        Assert.Equal(501, fixture.ArchiveLimit);
        Assert.Empty(fixture.Audit);
        Assert.Equal(before, TemporaryDirectories());

        using var pdf = await client.GetAsync($"/Admin/FinancialDocuments/{fixture.Document.DocumentId}/document.pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestData.Pdf, await pdf.Content.ReadAsByteArrayAsync());
        Assert.True(pdf.Headers.CacheControl?.Private);

        using var csv = await PostAsync(client, Exports + "?handler=Reconciliation", token,
            ("from", "2026-03-29"), ("to", "2026-03-29"));
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero), fixture.CsvFrom);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero), fixture.CsvTo);
        Assert.Equal("export.payment-reconciliation", Assert.Single(fixture.Audit).Action);
        Assert.Equal(1, fixture.ArchiveReads);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(AccessRole.Customer, false, false)]
    [InlineData(AccessRole.Requester, false, false)]
    [InlineData(AccessRole.Admin, true, false)]
    [InlineData(AccessRole.Customer, false, true)]
    public async Task NonAdminOrInvalidSessionCannotStartArchive(
        AccessRole? role, bool invalidIdentity, bool staleAdminRole)
    {
        var fixture = new TestData(role);
        using var app = CreateApp(fixture);
        using var client = Client(app, fixture, invalidIdentity, staleAdminRole);
        using var response = await PostAsync(client, Archive, "invalid", ("ArchiveMonth", "2026-03"));
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
        Assert.Equal(0, fixture.ArchiveReads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("malformed")]
    public async Task PostEnforcesAntiforgery(string? token)
    {
        var fixture = new TestData(AccessRole.Admin);
        using var app = CreateApp(fixture);
        using var client = Client(app, fixture);
        await AntiforgeryTokenAsync(client);
        using var response = await PostAsync(client, Archive, token, ("ArchiveMonth", "2026-03"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.ArchiveReads);
    }

    [Fact]
    public async Task InvalidDatesRedisplayWithBoundValuesAndReloadServiceUnits()
    {
        var fixture = new TestData(AccessRole.Admin);
        using var app = CreateApp(fixture);
        using var client = Client(app, fixture);
        var token = await AntiforgeryTokenAsync(client);

        using var response = await PostAsync(client, Archive, token,
            ("ArchiveFrom", "bad-date"), ("ArchiveTo", "2026-03-20"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("value=\"bad-date\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"2026-03-20\"", html, StringComparison.Ordinal);
        Assert.Contains("31", html, StringComparison.Ordinal);
        Assert.Equal(2, fixture.ServiceUnitReads);
        Assert.Equal(0, fixture.ArchiveReads);
    }

    [Theory]
    [InlineData("empty", HttpStatusCode.OK)]
    [InlineData("legacy", HttpStatusCode.Conflict)]
    [InlineData("partial-render-failure", HttpStatusCode.Conflict)]
    [InlineData("too-many", HttpStatusCode.UnprocessableEntity)]
    [InlineData("oversize-pdf", HttpStatusCode.UnprocessableEntity)]
    [InlineData("unexpected", HttpStatusCode.InternalServerError)]
    [InlineData("duplicate", HttpStatusCode.Conflict)]
    [InlineData("unsafe-name", HttpStatusCode.Conflict)]
    [InlineData("invalid-pdf", HttpStatusCode.Conflict)]
    public async Task ErrorOrEmptySelectionNeverSendsAFileAndAllowsRetry(
        string scenario, HttpStatusCode status)
    {
        var fixture = new TestData(AccessRole.Admin) { Scenario = scenario };
        var completion = new ResponseGate();
        using var app = CreateApp(fixture, completion);
        using var client = Client(app, fixture);
        var token = await AntiforgeryTokenAsync(client);
        var before = TemporaryDirectories();
        using var response = await PostAsync(client, Archive, token, ("ArchiveMonth", "2026-03"));
        Assert.Equal(status, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.NotEqual("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("sensitive test diagnostics", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        if (scenario is "too-many" or "empty")
        {
            Assert.Equal(0, fixture.Renders);
        }

        if (scenario == "partial-render-failure")
        {
            Assert.Equal(2, fixture.Renders);
        }

        Assert.Equal(before, TemporaryDirectories());
        fixture.Scenario = "valid";
        using var retry = await PostObservedArchiveAsync(client, token, completion);
        Assert.Equal("application/zip", retry.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, TemporaryDirectories());
    }

    [Fact]
    public async Task ZipLeaseStaysHeldThroughResponseAndDisconnectCleansUp()
    {
        var fixture = new TestData(AccessRole.Admin);
        var gate = new ResponseGate();
        using var app = CreateApp(fixture, gate);
        app.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var address = new Uri(Assert.Single(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses));
        Socket? transport = null;
        using var client = Client(app, fixture, address: address, onConnect: socket => transport = socket);
        using var competitor = Client(app, fixture, address: address);
        var token = await AntiforgeryTokenAsync(client);
        var otherToken = await AntiforgeryTokenAsync(competitor);
        var before = TemporaryDirectories();

        using var request = new HttpRequestMessage(HttpMethod.Post, Archive)
        {
            Content = Form(token, ("ArchiveMonth", "2026-03"))
        };
        request.Headers.Add("X-Test-Hold-Zip", "1");
        using var timeout = new CancellationTokenSource(Timeout);
        using var first = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        try
        {
            await gate.Sending.Task.WaitAsync(Timeout);
            Assert.Equal("application/zip", first.Content.Headers.ContentType?.MediaType);
            Assert.Single(TemporaryDirectories().Except(before));

            using var blocked = await PostAsync(competitor, Archive, otherToken, ("ArchiveMonth", "2026-03"));
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            Assert.Equal(1, fixture.ArchiveReads);

            using var pdf = await competitor.GetAsync(
                $"/Admin/FinancialDocuments/{fixture.Document.DocumentId}/document.pdf");
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

            Assert.NotNull(transport);
            await using var partial = await first.Content.ReadAsStreamAsync(timeout.Token);
            Assert.Equal((int)'P', partial.ReadByte());
            transport.LingerState = new LingerOption(enable: true, seconds: 0);
            transport.Close(0);
            var interrupted = await Record.ExceptionAsync(async () =>
            {
                await partial.CopyToAsync(Stream.Null, timeout.Token);
            });
            Assert.True(interrupted is IOException or HttpRequestException or ObjectDisposedException,
                $"Expected interrupted HTTP body, received {interrupted?.GetType().Name ?? "no error"}.");
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        await gate.Completed.Task.WaitAsync(Timeout);
        Assert.Equal(before, TemporaryDirectories());
        using var retry = await PostAsync(competitor, Archive, otherToken, ("ArchiveMonth", "2026-03"));
        Assert.Equal("application/zip", retry.Content.Headers.ContentType?.MediaType);
    }

    private WebApplicationFactory<Program> CreateApp(TestData data, ResponseGate? gate = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            Replace<IAccessSessionQueries>();
            Replace<IServiceUnitQueries>();
            Replace<IFinancialDocumentArchiveQueries>();
            Replace<IFinancialDocumentQueries>();
            Replace<IFinancialDocumentPdfRenderer>();
            Replace<IPaymentReconciliationExportQueries>();
            Replace<IAuditTrail>();
            if (gate is not null)
            {
                services.AddSingleton<IStartupFilter>(gate);
            }

            void Replace<T>() where T : class
            {
                services.RemoveAll<T>();
                services.AddSingleton((T)(object)data);
            }
        }));

    private static HttpClient Client(WebApplicationFactory<Program> app, TestData data,
        bool invalidIdentity = false, bool staleAdminRole = false, Uri? address = null,
        Action<Socket>? onConnect = null)
    {
        var client = address is null
            ? app.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            })
            : new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                MaxResponseDrainSize = 0,
                UseProxy = false,
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                        onConnect?.Invoke(socket);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            })
            { BaseAddress = address };

        if (data.Session is { } session)
        {
            const string scheme = CookieAuthenticationDefaults.AuthenticationScheme;
            var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(scheme);
            var principal = invalidIdentity
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], scheme))
                : AccessClaimsPrincipalFactory.Create(staleAdminRole
                    ? session with { Roles = [AccessRole.Admin] }
                    : session, scheme);
            var cookie = options.TicketDataFormat.Protect(new AuthenticationTicket(principal, scheme));
            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={cookie}");
        }

        return client;
    }

    private static async Task<string> AntiforgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync(Exports);
        var token = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(token.Success);
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, string? token, params (string Name, string Value)[] values)
    {
        using var form = Form(token, values);
        return await client.PostAsync(path, form);
    }

    private static async Task<HttpResponseMessage> PostObservedArchiveAsync(
        HttpClient client, string token, ResponseGate completion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Archive)
        {
            Content = Form(token, ("ArchiveMonth", "2026-03"))
        };
        request.Headers.Add("X-Test-Observe-Completion", "1");
        var response = await client.SendAsync(request);
        try
        {
            await completion.Completed.Task.WaitAsync(Timeout);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static FormUrlEncodedContent Form(string? token, params (string Name, string Value)[] values)
    {
        var fields = values.Select(item => KeyValuePair.Create(item.Name, item.Value)).ToList();
        if (token is not null)
        {
            fields.Add(KeyValuePair.Create("__RequestVerificationToken", token));
        }

        return new FormUrlEncodedContent(fields);
    }

    private static string[] TemporaryDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), FinancialDocumentArchiveFile.DirectoryPrefix + "*")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed class TestData(AccessRole? role) : IAccessSessionQueries, IServiceUnitQueries,
        IFinancialDocumentArchiveQueries, IFinancialDocumentQueries, IFinancialDocumentPdfRenderer,
        IPaymentReconciliationExportQueries, IAuditTrail
    {
        public static readonly byte[] Pdf = "%PDF-1.7 syntheticky test rendereru"u8.ToArray();

        public AccessSessionSnapshot? Session { get; } = role is { } accessRole
            ? new(Guid.NewGuid(), "Test Session", "session@example.test", AccessUserStatus.Active, [accessRole])
            : null;
        public FinancialDocument Document { get; } = CreateDocument(1);
        public List<AuditEntry> Audit { get; } = [];
        public string Scenario { get; set; } = "valid";
        public int Renders { get; private set; }
        public int ArchiveReads { get; private set; }
        public int ServiceUnitReads { get; private set; }
        public int ArchiveLimit { get; private set; }
        public DateTimeOffset ArchiveFrom { get; private set; }
        public DateTimeOffset ArchiveTo { get; private set; }
        public DateTimeOffset? CsvFrom { get; private set; }
        public DateTimeOffset? CsvTo { get; private set; }

        public Task<AccessSessionSnapshot?> FindAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Session?.UserId == userId ? Session : null);

        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
            CancellationToken cancellationToken = default)
        {
            ServiceUnitReads++;
            return Task.FromResult<IReadOnlyList<ServiceUnitAdministrationListItem>>([]);
        }

        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>> ListAssignmentsForUserAsync(
            Guid userId, bool includeRevoked = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceUnitReadModel?> FindActiveAsync(
            Guid serviceUnitId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
            Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FinancialDocument>> ListIssuedAsync(
            DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int limit, CancellationToken cancellationToken)
        {
            ArchiveReads++;
            ArchiveFrom = fromUtc;
            ArchiveTo = toUtcExclusive;
            ArchiveLimit = limit;
            return Task.FromResult<IReadOnlyList<FinancialDocument>>(Scenario switch
            {
                "empty" => [],
                "partial-render-failure" => [Document, CreateDocument(2)],
                "too-many" => Enumerable.Repeat(Document, 501).ToArray(),
                "duplicate" => [Document, Document],
                _ => [Document]
            });
        }

        public FinancialDocumentPdfFile Render(FinancialDocument document)
        {
            Renders++;
            if (Scenario == "legacy" || (Scenario == "partial-render-failure" && Renders == 2))
            {
                throw new FinancialDocumentRenderUnavailableException(
                    Scenario == "legacy" ? FinancialDocumentRenderUnavailableReason.LegacyIncompleteSnapshot :
                        FinancialDocumentRenderUnavailableReason.LayoutOverflow, "sensitive test diagnostics");
            }

            if (Scenario == "unexpected")
            {
                throw new InvalidOperationException("sensitive test diagnostics");
            }

            var bytes = Scenario switch
            {
                "oversize-pdf" => new byte[2 * 1024 * 1024 + 1],
                "invalid-pdf" => "not a PDF"u8.ToArray(),
                _ => Pdf
            };
            var name = Scenario == "unsafe-name"
                ? "../other.pdf"
                : $"doklad-o-uhrade-{document.DocumentNumber}.pdf";
            return new FinancialDocumentPdfFile(bytes, name);
        }

        public Task<FinancialDocument?> FindByIdForAdminAsync(
            Guid documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(documentId == Document.DocumentId ? Document : null);

        public Task<FinancialDocument?> FindByIdForCustomerAsync(
            Guid documentId, Guid customerUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, Guid>> FindDocumentIdsBySourceForCustomerAsync(
            Guid customerUserId, FinancialDocumentSourceType sourceType, IEnumerable<Guid> sourceIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PaymentReconciliationExportRow>> ListAsync(
            DateTimeOffset? fromInclusive, DateTimeOffset? toExclusive, int maximumRows,
            CancellationToken cancellationToken = default)
        {
            CsvFrom = fromInclusive;
            CsvTo = toExclusive;
            return Task.FromResult<IReadOnlyList<PaymentReconciliationExportRow>>([]);
        }

        public void Stage(AuditEntry entry) => Audit.Add(entry);

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Audit.Add(entry);
            return Task.CompletedTask;
        }

        private static FinancialDocument CreateDocument(int index)
        {
            var timestamp = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
            return FinancialDocument.CreateManualCreditTopUp(
                Guid.NewGuid(), $"FUA-2026-{index:000000}", Guid.NewGuid(),
                new FinancialDocumentCustomerSnapshot(Guid.NewGuid(), "Synthetic customer", null),
                2_500, "CZK", timestamp, timestamp,
                new FinancialDocumentIssuerSnapshot("Test University", "Test Faculty", "Test Street",
                    "Test City", "CZ", "00000000", "CZ00000000", "issuer@example.test"),
                FinancialDocumentTaxPolicy.CreateApprovedSnapshot(2_500));
        }
    }

    private sealed class ResponseGate : IStartupFilter
    {
        public TaskCompletionSource Sending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                var hold = context.Request.Headers.ContainsKey("X-Test-Hold-Zip");
                if (hold || context.Request.Headers.ContainsKey("X-Test-Observe-Completion"))
                {
                    context.Response.OnCompleted(() =>
                    {
                        Completed.TrySetResult();
                        return Task.CompletedTask;
                    });
                }

                if (!hold)
                {
                    await continuation(context);
                    return;
                }

                var original = context.Response.Body;
                await using var held = new HeldResponseStream(original, this, context.RequestAborted);
                context.Response.Body = held;
                try
                {
                    await continuation(context);
                }
                finally
                {
                    context.Response.Body = original;
                }
            });
            next(app);
        };

        private sealed class HeldResponseStream(Stream inner, ResponseGate gate, CancellationToken aborted) : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => inner.Flush();

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            {
                await inner.WriteAsync(buffer[..1], cancellationToken);
                await inner.FlushAsync(cancellationToken);
                gate.Sending.TrySetResult();
                await gate.Release.Task.WaitAsync(aborted);
                await inner.WriteAsync(buffer[1..], cancellationToken);
            }

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
