using System.Net;
using System.Security.Cryptography;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
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
using Microsoft.Extensions.Hosting;
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
    public async Task PublicLayout_DoesNotReferenceUnproducedScopedCssBundle()
    {
        using var client = CreateClient(_factory);
        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            "/FuaPay.Web.styles.css",
            html,
            StringComparison.Ordinal);
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
    public async Task CardJobReversePost_WithoutAntiforgeryToken_ReturnsBadRequest()
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
        var protectedTicket = cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["operationId"] = Guid.NewGuid().ToString(),
                ["originalPaymentId"] = Guid.NewGuid().ToString(),
                ["reason"] = "Approved full return"
            });
        using var response = await client.PostAsync(
            "/Admin/Payments?handler=Reverse",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, sessionQueries.CallCount);
        Assert.Equal(session.UserId, sessionQueries.LastUserId);
    }

    [Fact]
    public async Task ManualCreditTopUpPost_ByCustomerIsDeniedBeforeHandler()
    {
        var session = new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Testovací zákazník",
            "customer@example.cz",
            AccessUserStatus.Active,
            [AccessRole.Customer]);
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
        var protectedTicket = cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["ManualTopUp.CommandId"] = Guid.NewGuid().ToString(),
                ["ManualTopUp.OwnerId"] = session.UserId.ToString(),
                ["ManualTopUp.AmountCrowns"] = "100",
                ["ManualTopUp.Note"] = "Unauthorized attempt"
            });

        using var response = await client.PostAsync(
            "/Admin/Credit?handler=ManualTopUp",
            content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(
            "/?ReturnUrl=%2FAdmin%2FCredit",
            response.Headers.Location?.PathAndQuery,
            StringComparison.Ordinal);
        Assert.Equal(1, sessionQueries.CallCount);
        Assert.Equal(session.UserId, sessionQueries.LastUserId);
    }

    [Fact]
    public async Task ManualCreditTopUpPost_WithoutAntiforgeryToken_ReturnsBadRequest()
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
        var protectedTicket = cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["ManualTopUp.CommandId"] = Guid.NewGuid().ToString(),
                ["ManualTopUp.OwnerId"] = Guid.NewGuid().ToString(),
                ["ManualTopUp.AmountCrowns"] = "100",
                ["ManualTopUp.Note"] = "Missing antiforgery token"
            });

        using var response = await client.PostAsync(
            "/Admin/Credit?handler=ManualTopUp",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, sessionQueries.CallCount);
        Assert.Equal(session.UserId, sessionQueries.LastUserId);
    }

    [Fact]
    public async Task PrintCredentialPost_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        var session = new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Testovací zákazník",
            "student@tul.cz",
            AccessUserStatus.Active,
            [AccessRole.Customer]);
        var sessionQueries = new RecordingAccessSessionQueries(session);
        using var configuredFactory =
            _factory.WithWebHostBuilder(
                builder => builder.ConfigureTestServices(
                    services =>
                    {
                        services.RemoveAll<IAccessSessionQueries>();
                        services.AddSingleton<IAccessSessionQueries>(sessionQueries);
                    }));
        var cookieOptions = configuredFactory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = AccessClaimsPrincipalFactory.Create(
            session,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var protectedTicket = cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.PrintCode"] = "123456",
                ["Input.Confirmation"] = "123456",
                ["ownerId"] = Guid.NewGuid().ToString()
            });

        using var response = await client.PostAsync(
            "/Customer/PrintCredential?handler=Set",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, sessionQueries.CallCount);
        Assert.Equal(session.UserId, sessionQueries.LastUserId);
    }

    [Fact]
    public async Task DisabledPrintCredentialPage_ReturnsNotFoundWithoutPepperOrHashing()
    {
        var session = new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Testovací zákazník",
            "student@tul.cz",
            AccessUserStatus.Active,
            [AccessRole.Customer]);
        var sessionQueries = new RecordingAccessSessionQueries(session);
        var hasher = new CountingPrintCodeHasher();
        using var baseFactory = new ConfiguredWebApplicationFactory(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:FuaPay"] =
                    "Host=localhost;Database=unused;" +
                    "Username=unused;Password=unused",
                ["PrintCredentials:Enabled"] = "false"
            });
        using var configuredFactory = baseFactory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<IAccessSessionQueries>();
                    services.AddSingleton<IAccessSessionQueries>(
                        sessionQueries);
                    services.RemoveAll<IPrintCodeHasher>();
                    services.AddSingleton<IPrintCodeHasher>(hasher);
                }));
        var cookieOptions = configuredFactory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = AccessClaimsPrincipalFactory.Create(
            session,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var protectedTicket = cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var response = await client.GetAsync(
            "/Customer/PrintCredential");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, sessionQueries.CallCount);
        Assert.Equal(0, hasher.HashCalls);
        Assert.Equal(0, hasher.VerifyCalls);
    }

    [Fact]
    public async Task PrintCredentialPage_WithStaleActiveCredentialStillOffersRevocation()
    {
        var session = new AccessSessionSnapshot(
            Guid.NewGuid(),
            "Testovací zákazník",
            null,
            AccessUserStatus.Active,
            [AccessRole.Customer]);
        var sessionQueries = new RecordingAccessSessionQueries(session);
        var repository = new StalePrintCredentialRepository(session.UserId);
        using var baseFactory = new ConfiguredWebApplicationFactory(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:FuaPay"] =
                    "Host=localhost;Database=unused;" +
                    "Username=unused;Password=unused",
                ["PrintPayments:Enabled"] = "true",
                ["PrintPayments:Sources:0:PrintSourceId"] =
                    Guid.NewGuid().ToString("D"),
                ["PrintPayments:Sources:0:CredentialSha256"] =
                    new string('a', 64),
                ["PrintCredentials:Enabled"] = "true",
                ["PrintCredentials:PepperBase64"] =
                    Convert.ToBase64String(new byte[32])
            });
        using var configuredFactory = baseFactory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<IAccessSessionQueries>();
                    services.AddSingleton<IAccessSessionQueries>(
                        sessionQueries);
                    services.RemoveAll<IPrintCredentialRepository>();
                    services.AddSingleton<IPrintCredentialRepository>(
                        repository);
                }));
        var cookieOptions = configuredFactory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = AccessClaimsPrincipalFactory.Create(
            session,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(
            principal,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var protectedTicket = cookieOptions.TicketDataFormat.Protect(ticket);

        using var client = CreateClient(configuredFactory);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieOptions.Cookie.Name}={protectedTicket}");
        using var response = await client.GetAsync(
            "/Customer/PrintCredential");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("handler=Revoke", html, StringComparison.Ordinal);
        Assert.Contains(
            "name=\"__RequestVerificationToken\"",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("handler=Set", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            StalePrintCredentialRepository.StaleEmail,
            html,
            StringComparison.OrdinalIgnoreCase);
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
    public async Task CsobDisabled_FormActionAllowsOnlySelf()
    {
        using var client = CreateClient(_factory);
        using var response = await client.GetAsync("/");

        Assert.Equal(
            "form-action 'self'",
            GetContentSecurityPolicyDirective(
                response,
                "form-action"));
    }

    [Theory]
    [InlineData(
        "Staging",
        "form-action 'self' https://iapi.iplatebnibrana.csob.cz https://iplatebnibrana.csob.cz")]
    [InlineData(
        "Production",
        "form-action 'self' https://api.platebnibrana.csob.cz https://platebnibrana.csob.cz")]
    public async Task ActiveCsobProvider_FormActionUsesExactValidatedBrowserBoundary(
        string environmentName,
        string expectedDirective)
    {
        using var externalFiles =
            new TemporaryDirectory("fua-pay-csp-csob");
        var privateKeyPath = Path.Combine(
            externalFiles.Path,
            "merchant.key");
        var publicKeyPath = Path.Combine(
            externalFiles.Path,
            "gateway.pub");
        WriteTestCsobKeys(privateKeyPath, publicKeyPath);

        using var factory =
            new ConfiguredWebApplicationFactory(
                environmentName,
                CreateCsobSettings(
                    environmentName,
                    externalFiles.Path,
                    privateKeyPath,
                    publicKeyPath));
        using var client = CreateClient(
            factory,
            new Uri("https://fuapay.example.test"));
        using var response = await client.GetAsync("/");

        Assert.Equal(
            expectedDirective,
            GetContentSecurityPolicyDirective(
                response,
                "form-action"));
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
        Assert.Equal(
            "style-src 'self'",
            GetContentSecurityPolicyDirective(
                acceptedResponse,
                "style-src"));
        Assert.DoesNotContain(
            "unsafe-inline",
            contentSecurityPolicy,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "unsafe-hashes",
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

    private static IReadOnlyDictionary<string, string?>
        CreateCsobSettings(
            string environmentName,
            string keyRingPath,
            string privateKeyPath,
            string publicKeyPath)
    {
        var isProduction = Environments.Production.Equals(
            environmentName,
            StringComparison.OrdinalIgnoreCase);

        return new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "fuapay.example.test",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["ConnectionStrings:FuaPay"] =
                "Host=localhost;Database=unused;" +
                "Username=unused;Password=unused",
            ["Entra:Enabled"] = isProduction.ToString(),
            ["Entra:TenantId"] =
                "11111111-1111-1111-1111-111111111111",
            ["Entra:ClientId"] =
                "22222222-2222-2222-2222-222222222222",
            ["Entra:ClientSecret"] = "test-only-secret",
            ["Payments:Provider"] = "Csob",
            ["Csob:Enabled"] = "true",
            ["Csob:ApiBaseUrl"] = isProduction
                ? "https://api.platebnibrana.csob.cz/"
                : "https://iapi.iplatebnibrana.csob.cz/",
            ["Csob:MerchantId"] = "M123456789",
            ["Csob:PrivateKeyPath"] = privateKeyPath,
            ["Csob:GatewayPublicKeyPath"] = publicKeyPath,
            ["Csob:ReturnUrl"] =
                "https://fuapay.example.test/payments/csob/return"
        };
    }

    private static string GetContentSecurityPolicyDirective(
        HttpResponseMessage response,
        string directiveName)
    {
        var policy = GetSingleHeader(
            response,
            "Content-Security-Policy");

        return Assert.Single(
            policy.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries),
            directive => directive.StartsWith(
                $"{directiveName} ",
                StringComparison.Ordinal));
    }

    private static void WriteTestCsobKeys(
        string privateKeyPath,
        string publicKeyPath)
    {
        using var merchant = RSA.Create(2048);
        using var gateway = RSA.Create(2048);

        File.WriteAllText(
            privateKeyPath,
            merchant.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(
            publicKeyPath,
            gateway.ExportSubjectPublicKeyInfoPem());
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

    private sealed class CountingPrintCodeHasher : IPrintCodeHasher
    {
        public int HashCalls { get; private set; }

        public int VerifyCalls { get; private set; }

        public string Hash(string printCode)
        {
            HashCalls++;
            return "unused";
        }

        public bool Verify(string hash, string printCode)
        {
            VerifyCalls++;
            return false;
        }
    }

    private sealed class StalePrintCredentialRepository :
        IPrintCredentialRepository
    {
        public const string StaleEmail = "stale-profile@example.cz";

        private readonly PrintCredential _credential;

        public StalePrintCredentialRepository(Guid ownerId)
        {
            _credential = new PrintCredential(
                ownerId,
                StaleEmail,
                "unused-hash",
                new DateTimeOffset(
                    2026,
                    9,
                    14,
                    8,
                    0,
                    0,
                    TimeSpan.Zero));
        }

        public Task<PrintCredential?> FindByOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintCredential?>(
                ownerId == _credential.OwnerId
                    ? _credential
                    : null);

        public Task<PrintCredentialAuthenticationCandidate?>
            FindAuthenticationCandidateAsync(
                string normalizedEmail,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintCredentialAuthenticationCandidate?>(null);

        public Task<long> CountAccessUsersByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);

        public void Add(PrintCredential credential) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
