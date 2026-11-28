using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Xunit.Abstractions;

namespace FuaPay.DatabaseTests;

public sealed partial class AccountingWorkbookPersistenceTests(
    WebApplicationFactory<Program> factory, ITestOutputHelper output)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Exports = "/Admin/Exports";
    private const string Handler = Exports + "?handler=AccountingWorkbook";
    private static readonly DateTimeOffset PostedAt = new(2026, 2, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ExportUsesOneReadOnlySnapshotAndAuditsOutsideIt()
    {
        var owner = Guid.NewGuid();
        var late = Guid.NewGuid();
        var observer = new SnapshotObserver();
        try
        {
            await SeedAsync(owner, 1_500, "Synthetic opening report row");
            using var app = CreateApp(owner, observer);
            using var client = CreateClient(app, owner);
            var token = await TokenAsync(client);
            using var form = Form(token);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var request = client.PostAsync(Handler, form);
            try
            {
                await observer.Ready.Task.WaitAsync(Timeout);
                Assert.Equal("repeatable read|on", observer.Mode);
                await SeedAsync(late, 3_500, "Synthetic concurrently committed row");
            }
            finally
            {
                observer.Release.TrySetResult();
            }

            using var response = await request.WaitAsync(Timeout);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var file = new ZipArchive(new MemoryStream(bytes));
            XNamespace sx = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheets = Xml(file, "xl/workbook.xml").Descendants(sx + "sheet")
                .Select(item => (string?)item.Attribute("name"));
            Assert.Equal(["Platby ČSOB", "Přehled", "Pohyby kreditu", "Zůstatky", "Zakázky", "Kontroly", "Střediska"], sheets);
            var movementText = Xml(file, "xl/worksheets/sheet3.xml").Descendants(sx + "t").Select(item => item.Value);
            Assert.Contains("Synthetic opening report row", movementText);
            Assert.DoesNotContain("Synthetic concurrently committed row", movementText);
            Assert.Equal(1, observer.AuditWrites);
            Assert.True(observer.AuditAfterSnapshot);

            await using var readback = factory.Services.CreateAsyncScope();
            var db = readback.ServiceProvider.GetRequiredService<FuaPayDbContext>();
            var actual = await db.Database.SqlQuery<long>($"""
                SELECT count(*) AS "Value" FROM audit.events
                WHERE actor_user_id = {owner} AND action = 'export.accounting-workbook'
                """).SingleAsync();
            Assert.Equal(1, actual);
            var movements = await db.Database.SqlQuery<long>($"""
                SELECT count(*) AS "Value" FROM credits.movements AS m
                JOIN credits.accounts AS a ON a.id = m.account_id WHERE a.owner_id IN ({owner}, {late})
                """).SingleAsync();
            Assert.Equal(2, movements);
            output.WriteLine($"PG integration workbook: {bytes.Length} bytes, HTTP {timer.ElapsedMilliseconds} ms, two synthetic rows in DB and one visible to report.");
        }
        finally
        {
            await DeleteFixturesAsync(owner, late);
        }
    }

    [Fact]
    public async Task AuditFailurePreventsSendingFileAndReleasesExportSlot()
    {
        var owner = Guid.NewGuid();
        try
        {
            await SeedAsync(owner, 2_000, "Synthetic failure report row");
            var audit = new FailingAudit();
            using var app = CreateApp(owner, audit: audit);
            using var client = CreateClient(app, owner);
            var token = await TokenAsync(client);
            for (var i = 0; i < 2; i++)
            {
                using var form = Form(token);
                using var response = await client.PostAsync(Handler, form);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Null(response.Content.Headers.ContentDisposition);
                Assert.DoesNotContain("Synthetic sensitive diagnostic", await response.Content.ReadAsStringAsync(),
                    StringComparison.Ordinal);
            }

            Assert.Equal(2, audit.Attempts);
        }
        finally
        {
            await DeleteFixturesAsync(owner);
        }
    }

    private WebApplicationFactory<Program> CreateApp(Guid owner, SnapshotObserver? observer = null,
        FailingAudit? audit = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccessSessionQueries>();
            services.AddSingleton<IAccessSessionQueries>(new FixedAdmin(owner));
            if (observer is not null)
            {
                services.ConfigureDbContext<FuaPayDbContext>(options => options.AddInterceptors(observer));
            }

            if (audit is not null)
            {
                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail>(audit);
            }
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> app, Guid owner)
    {
        const string scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        var cookie = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var ticket = new AuthenticationTicket(AccessClaimsPrincipalFactory.Create(FixedAdmin.Snapshot(owner), scheme), scheme);
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie.Cookie.Name}={cookie.TicketDataFormat.Protect(ticket)}");
        return client;
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync(Exports);
        var result = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(result);
        return result;
    }

    private static FormUrlEncodedContent Form(string token) =>
        new([new("WorkbookMonth", "2026-02"), new("__RequestVerificationToken", token)]);

    private async Task SeedAsync(Guid owner, long amount, string description)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var profile = new AccessUser(owner, "Synthetic report actor", "accounting@example.test", PostedAt);
        await services.GetRequiredService<IAccessUserRepository>().AddAsync(profile,
            new ExternalIdentityKey("accounting-tests", "synthetic", owner.ToString("N")), CancellationToken.None);
        var account = new CreditAccount(Guid.NewGuid(), owner);
        account.Credit(Guid.NewGuid(), new Money(amount), PostedAt, description);
        await services.GetRequiredService<ICreditAccountRepository>().AddAsync(account, CancellationToken.None);
    }

    private async Task DeleteFixturesAsync(params Guid[] owners)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
        foreach (var owner in owners)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM audit.events WHERE actor_user_id = {owner};
                DELETE FROM credits.movements WHERE account_id IN (SELECT id FROM credits.accounts WHERE owner_id = {owner});
                DELETE FROM credits.accounts WHERE owner_id = {owner};
                DELETE FROM access.external_identities WHERE user_id = {owner};
                DELETE FROM access.users WHERE id = {owner};
                """);
        }
    }

    private static XDocument Xml(ZipArchive file, string name)
    {
        using var entry = file.GetEntry(name)!.Open();
        return XDocument.Load(entry);
    }

    private sealed class FixedAdmin(Guid owner) : IAccessSessionQueries
    {
        public static AccessSessionSnapshot Snapshot(Guid id) =>
            new(id, "Synthetic report actor", "accounting@example.test", AccessUserStatus.Active, [AccessRole.Admin]);

        public Task<AccessSessionSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccessSessionSnapshot?>(owner == userId ? Snapshot(owner) : null);
    }

    private sealed class SnapshotObserver : DbCommandInterceptor
    {
        private DbTransaction? _reportTransaction;
        private bool _observed;
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Mode { get; private set; }
        public int AuditWrites { get; private set; }
        public bool AuditAfterSnapshot { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO audit.events", StringComparison.Ordinal))
            {
                AuditWrites++;
                AuditAfterSnapshot = !ReferenceEquals(command.Transaction, _reportTransaction) &&
                    eventData.Context?.Database.CurrentTransaction is null;
            }
            else if (command.CommandText.Contains("statement_timestamp()", StringComparison.Ordinal))
            {
                _reportTransaction = command.Transaction ?? throw new InvalidOperationException("No SQL snapshot transaction.");
            }
            else if (!_observed && _reportTransaction is not null && ReferenceEquals(_reportTransaction, command.Transaction))
            {
                _observed = true;
                await using var settings = command.Connection!.CreateCommand();
                settings.Transaction = _reportTransaction;
                settings.CommandText = "SELECT current_setting('transaction_isolation') || '|' || current_setting('transaction_read_only')";
                Mode = (string?)await settings.ExecuteScalarAsync(cancellationToken);
                Ready.TrySetResult();
                await Release.Task.WaitAsync(Timeout, cancellationToken);
            }

            return result;
        }
    }

    private sealed class FailingAudit : IAuditTrail
    {
        public int Attempts { get; private set; }

        public void Stage(AuditEntry entry) => throw new NotSupportedException();

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("Synthetic sensitive diagnostic");
        }
    }
}
