using System.Net;
using System.Text.RegularExpressions;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Tests.Hosting;

public sealed class AccountingWorkbookHttpTests(ConfiguredWebApplicationFactory factory)
    : IClassFixture<ConfiguredWebApplicationFactory>
{
    private const string Page = "/Admin/Exports";
    private const string Export = Page + "?handler=AccountingWorkbook";

    [Fact]
    public async Task AdminPageOffersAccountingWorkbookAndFourCsvHandlers()
    {
        using var app = App(AccessRole.Admin, out var session);
        using var client = Client(app, session);
        var html = await client.GetStringAsync(Page);
        foreach (var handler in new[] { "Jobs", "Credit", "Payments", "Reconciliation", "AccountingWorkbook" })
        {
            Assert.Contains($"handler={handler}", html, StringComparison.Ordinal);
        }
        Assert.Contains("name=\"WorkbookMonth\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPeriodReturnsValidationWithInputPreserved()
    {
        using var app = App(AccessRole.Admin, out var session);
        using var client = Client(app, session);
        var html = await client.GetStringAsync(Page);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["WorkbookFrom"] = "2026-02-30",
            ["WorkbookTo"] = "2026-03-10",
            ["__RequestVerificationToken"] = token
        });
        using var response = await client.PostAsync(Export, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("value=\"2026-03-10\"", await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccessRole.Admin, HttpStatusCode.BadRequest)]
    [InlineData(AccessRole.Customer, HttpStatusCode.Redirect)]
    [InlineData(AccessRole.Requester, HttpStatusCode.Redirect)]
    [InlineData(null, HttpStatusCode.Redirect)]
    public async Task PostRequiresAdminAndToken(AccessRole? role, HttpStatusCode status)
    {
        using var app = App(role, out var session);
        using var client = Client(app, session);
        using var body = new FormUrlEncodedContent([new("WorkbookMonth", "2026-02")]);
        using var response = await client.PostAsync(Export, body);
        Assert.Equal(status, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    private WebApplicationFactory<Program> App(AccessRole? role, out AccessSessionSnapshot? session)
    {
        var account = role is { } value
            ? new AccessSessionSnapshot(Guid.NewGuid(), "Test session", "session@example.test",
                AccessUserStatus.Active, [value])
            : null;
        session = account;
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccessSessionQueries>();
            services.AddSingleton<IAccessSessionQueries>(new FixedSession(account));
            services.RemoveAll<IServiceUnitQueries>();
            services.AddSingleton<IServiceUnitQueries, EmptyUnits>();
        }));
    }

    private static HttpClient Client(WebApplicationFactory<Program> app, AccessSessionSnapshot? account)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        if (account is not null)
        {
            const string scheme = CookieAuthenticationDefaults.AuthenticationScheme;
            var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
            var ticket = new AuthenticationTicket(AccessClaimsPrincipalFactory.Create(account, scheme), scheme);
            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        }
        return client;
    }

    private sealed class FixedSession(AccessSessionSnapshot? account) : IAccessSessionQueries
    {
        public Task<AccessSessionSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(account?.UserId == userId ? account : null);
    }

    private sealed class EmptyUnits : IServiceUnitQueries
    {
        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceUnitAdministrationListItem>>([]);
        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>> ListAssignmentsForUserAsync(
            Guid userId, bool includeRevoked = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ServiceUnitReadModel?> FindActiveAsync(Guid serviceUnitId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
            Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
