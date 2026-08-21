using System.Net;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuaPay.Web.Tests.Hosting;

public sealed class SecurityPerimeterTests :
    IClassFixture<ConfiguredWebApplicationFactory>
{
    private readonly ConfiguredWebApplicationFactory _factory;

    public SecurityPerimeterTests(
        ConfiguredWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Privacy")]
    [InlineData("/Terms")]
    [InlineData("/css/base.css")]
    [InlineData("/css/shell.css")]
    [InlineData("/css/dashboard.css")]
    [InlineData("/css/components.css")]
    [InlineData("/css/features.css")]
    [InlineData("/css/responsive.css")]
    public async Task PublicEndpoint_WithoutAuthentication_ReturnsSuccess(
        string path)
    {
        using var client = CreateClient(_factory);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnannotatedSignOutPage_WithoutAuthentication_IsProtectedByFallbackPolicy()
    {
        using var client = CreateClient(_factory);
        using var response =
            await client.GetAsync("/Account/SignOut");

        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);

        var location =
            Assert.IsType<Uri>(response.Headers.Location);

        Assert.Contains(
            "/Development/SignIn",
            location.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedDynamicRouteWithExtension_ResynchronizesSession()
    {
        var userId = Guid.NewGuid();
        var sessionQueries = new RecordingAccessSessionQueries();
        using var configuredFactory =
            _factory.WithWebHostBuilder(
                builder => builder.ConfigureTestServices(
                    services =>
                    {
                        services.RemoveAll<IAccessSessionQueries>();
                        services.AddSingleton<IAccessSessionQueries>(
                            sessionQueries);
                        services.PostConfigure<RazorPagesOptions>(
                            options =>
                                options.Conventions.AddPageRoute(
                                    "/Account/SignOut",
                                    "/protected/report.csv"));
                    }));

        var cookieOptions = configuredFactory.Services
            .GetRequiredService<
                IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = AccessClaimsPrincipalFactory.Create(
            new AccessSessionSnapshot(
                userId,
                "Testovací uživatel",
                "test@example.cz",
                AccessUserStatus.Active,
                [AccessRole.Customer]),
            CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var protectedTicket =
            cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var response =
            await client.GetAsync("/protected/report.csv");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.True(
            response.Headers.CacheControl?.NoStore == true);
        Assert.Equal(1, sessionQueries.CallCount);
        Assert.Equal(userId, sessionQueries.LastUserId);
    }

    [Fact]
    public async Task DevelopmentSignInPost_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        using var client = CreateClient(_factory);
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["ProfileKey"] = "invalid-without-token"
            });
        using var response = await client.PostAsync(
            "/Development/SignIn?handler=SignIn",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task AdministrationExportPost_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        var session = new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Testovací administrátor",
            "admin@example.cz",
            AccessUserStatus.Active,
            [AccessRole.Admin]);
        var sessionQueries =
            new RecordingAccessSessionQueries(session);
        using var configuredFactory =
            _factory.WithWebHostBuilder(
                builder => builder.ConfigureTestServices(
                    services =>
                    {
                        services.RemoveAll<IAccessSessionQueries>();
                        services.AddSingleton<IAccessSessionQueries>(
                            sessionQueries);
                    }));

        var cookieOptions = configuredFactory.Services
            .GetRequiredService<
                IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = AccessClaimsPrincipalFactory.Create(
            session,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var protectedTicket =
            cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["from"] = "2026-01-01",
                ["to"] = "2026-01-31"
            });
        using var response = await client.PostAsync(
            "/Admin/Exports?handler=Jobs",
            content);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(1, sessionQueries.CallCount);
        Assert.Equal(session.UserId, sessionQueries.LastUserId);
    }

    [Fact]
    public void StagingSecurityCookies_UseSecurePolicies()
    {
        using var stagingFactory =
            new ConfiguredWebApplicationFactory(
                "Staging",
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FuaPay"] =
                        "Host=localhost;" +
                        "Database=unused;" +
                        "Username=unused;" +
                        "Password=unused"
                });

        var cookieOptions =
            stagingFactory.Services
                .GetRequiredService<
                    IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);
        var antiforgeryOptions =
            stagingFactory.Services
                .GetRequiredService<IOptions<AntiforgeryOptions>>()
                .Value;

        Assert.True(cookieOptions.Cookie.HttpOnly);
        Assert.Equal(
            CookieSecurePolicy.Always,
            cookieOptions.Cookie.SecurePolicy);
        Assert.Equal(
            SameSiteMode.Lax,
            cookieOptions.Cookie.SameSite);
        Assert.False(cookieOptions.SlidingExpiration);

        Assert.True(antiforgeryOptions.Cookie.HttpOnly);
        Assert.Equal(
            CookieSecurePolicy.Always,
            antiforgeryOptions.Cookie.SecurePolicy);
        Assert.Equal(
            SameSiteMode.Strict,
            antiforgeryOptions.Cookie.SameSite);
    }

    [Fact]
    public void StagingForwardedHeaders_TrustOnlyConfiguredProxy()
    {
        using var stagingFactory =
            new ConfiguredWebApplicationFactory(
                "Staging",
                new Dictionary<string, string?>
                {
                    ["Hosting:UseForwardedHeaders"] = "true",
                    ["Hosting:KnownProxies:0"] = "127.0.0.1",
                    ["ConnectionStrings:FuaPay"] =
                        "Host=localhost;" +
                        "Database=unused;" +
                        "Username=unused;" +
                        "Password=unused"
                });

        var options =
            stagingFactory.Services
                .GetRequiredService<
                    IOptions<ForwardedHeadersOptions>>()
                .Value;

        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal(
            IPAddress.Loopback,
            Assert.Single(options.KnownProxies));
        Assert.Equal(
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedHost |
            ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
    }

    [Fact]
    public async Task NonDevelopmentResponse_UsesHostFilteringAndSecurityHeaders()
    {
        using var keyRing =
            new TemporaryDirectory("fua-pay-production-test");
        using var hostedFactory =
            new ConfiguredWebApplicationFactory(
                "Staging",
                CreateHostedSettings(keyRing.Path));
        using var client = CreateClient(
            hostedFactory,
            new Uri("https://fuapay.example.test"));

        using var acceptedResponse =
            await client.GetAsync("/");

        Assert.Equal(
            HttpStatusCode.OK,
            acceptedResponse.StatusCode);
        Assert.Equal(
            "nosniff",
            GetSingleHeader(
                acceptedResponse,
                "X-Content-Type-Options"));
        Assert.Equal(
            "DENY",
            GetSingleHeader(
                acceptedResponse,
                "X-Frame-Options"));
        Assert.Equal(
            "strict-origin-when-cross-origin",
            GetSingleHeader(
                acceptedResponse,
                "Referrer-Policy"));
        Assert.Equal(
            "camera=(), geolocation=(), microphone=()",
            GetSingleHeader(
                acceptedResponse,
                "Permissions-Policy"));
        var contentSecurityPolicy =
            GetSingleHeader(
                acceptedResponse,
                "Content-Security-Policy");
        Assert.Contains(
            "default-src 'self'",
            contentSecurityPolicy,
            StringComparison.Ordinal);
        Assert.Contains(
            "base-uri 'none'",
            contentSecurityPolicy,
            StringComparison.Ordinal);

        var hsts =
            GetSingleHeader(
                acceptedResponse,
                "Strict-Transport-Security");
        Assert.Contains(
            "max-age=31536000",
            hsts,
            StringComparison.Ordinal);

        using var rejectedRequest =
            new HttpRequestMessage(HttpMethod.Get, "/");
        rejectedRequest.Headers.Host =
            "unexpected.example.test";
        using var rejectedResponse =
            await client.SendAsync(rejectedRequest);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            rejectedResponse.StatusCode);
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        Uri? baseAddress = null)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress =
                    baseAddress ??
                    new Uri("https://localhost")
            });
    }

    private static IReadOnlyDictionary<string, string?>
        CreateHostedSettings(string keyRingPath)
    {
        return new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "fuapay.example.test",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["ConnectionStrings:FuaPay"] =
                "Host=localhost;" +
                "Database=unused;" +
                "Username=unused;" +
                "Password=unused"
        };
    }

    private static string GetSingleHeader(
        HttpResponseMessage response,
        string name)
    {
        Assert.True(
            response.Headers.TryGetValues(
                name,
                out var values));

        return Assert.Single(values!);
    }

    private sealed class RecordingAccessSessionQueries :
        IAccessSessionQueries
    {
        private readonly AccessSessionSnapshot? _snapshot;
        private int _callCount;

        public RecordingAccessSessionQueries(
            AccessSessionSnapshot? snapshot = null)
        {
            _snapshot = snapshot;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public Guid? LastUserId { get; private set; }

        public Task<AccessSessionSnapshot?> FindAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastUserId = userId;
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_snapshot);
        }
    }
}
