using System.Net;

using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

namespace FuaPay.Web.Tests.Hosting;

public sealed class ExternalNetworkBoundaryTests
{
    [Fact]
    public async Task DevelopmentSimulatedMode_DoesNotRegisterExternalIntegrations()
    {
        using var factory = new ConfiguredWebApplicationFactory();
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/health/live");
        using var workerResponse = await client.GetAsync(
            "/health/workers/csob-reconciliation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, workerResponse.StatusCode);
        Assert.Contains(
            "\"status\":\"Disabled\"",
            await workerResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var schemes = factory.Services
            .GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemes.GetSchemeAsync(
            EntraAuthenticationDefaults.AuthenticationScheme));
        Assert.Null(factory.Services.GetService<ICsobGatewayClient>());
        Assert.DoesNotContain(
            factory.Services.GetServices<IHostedService>(),
            service => service is CsobPaymentReconciliationWorker);
    }

    [Fact]
    public async Task ProductionHealthProbes_DoNotContactEntraOrCsob()
    {
        using var externalFiles =
            new TemporaryDirectory("fua-pay-network-boundary");
        var privateKeyPath = Path.Combine(
            externalFiles.Path,
            "merchant.key");
        var publicKeyPath = Path.Combine(
            externalFiles.Path,
            "gateway.pub");
        File.WriteAllText(privateKeyPath, "test-only");
        File.WriteAllText(publicKeyPath, "test-only");

        var counter = new OutboundRequestCounter();
        using var productionFactory =
            new ConfiguredWebApplicationFactory(
                Environments.Production,
                CreateProductionSettings(
                    externalFiles.Path,
                    privateKeyPath,
                    publicKeyPath));
        using var instrumentedFactory =
            productionFactory.WithWebHostBuilder(
                builder => builder.ConfigureTestServices(
                    services =>
                    {
                        services.AddSingleton<
                            IHttpMessageHandlerBuilderFilter>(
                            new CountingHttpMessageHandlerBuilderFilter(
                                counter));
                        services.PostConfigure<OpenIdConnectOptions>(
                            EntraAuthenticationDefaults
                                .AuthenticationScheme,
                            options =>
                            {
                                options.Backchannel = new HttpClient(
                                    new CountingTerminalHandler(counter));
                            });
                    }));
        using var client = CreateClient(
            instrumentedFactory,
            new Uri("https://fuapay.example.test"));

        using var liveResponse = await client.GetAsync("/health/live");
        using var readyResponse = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            readyResponse.StatusCode);
        Assert.Equal(0, counter.Count);
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        Uri? baseAddress = null)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = baseAddress ?? new Uri("https://localhost")
            });
    }

    private static IReadOnlyDictionary<string, string?>
        CreateProductionSettings(
            string keyRingPath,
            string privateKeyPath,
            string publicKeyPath)
    {
        return new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "fuapay.example.test",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["ConnectionStrings:FuaPay"] =
                "Host=127.0.0.1;Port=1;Database=unused;" +
                "Username=unused;Password=unused;Timeout=1;" +
                "Command Timeout=1",
            ["Entra:Enabled"] = "true",
            ["Entra:TenantId"] =
                "11111111-1111-1111-1111-111111111111",
            ["Entra:ClientId"] =
                "22222222-2222-2222-2222-222222222222",
            ["Entra:ClientSecret"] = "test-only-secret",
            ["Payments:Provider"] = "Csob",
            ["Csob:Enabled"] = "true",
            ["Csob:ApiBaseUrl"] =
                "https://api.platebnibrana.csob.cz/",
            ["Csob:MerchantId"] = "M123456789",
            ["Csob:PrivateKeyPath"] = privateKeyPath,
            ["Csob:GatewayPublicKeyPath"] = publicKeyPath,
            ["Csob:ReturnUrl"] =
                "https://fuapay.example.test/payments/csob/return"
        };
    }

    private sealed class OutboundRequestCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Record()
        {
            Interlocked.Increment(ref _count);
        }
    }

    private sealed class CountingHttpMessageHandlerBuilderFilter :
        IHttpMessageHandlerBuilderFilter
    {
        private readonly OutboundRequestCounter _counter;

        public CountingHttpMessageHandlerBuilderFilter(
            OutboundRequestCounter counter)
        {
            _counter = counter;
        }

        public Action<HttpMessageHandlerBuilder> Configure(
            Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                next(builder);
                builder.AdditionalHandlers.Insert(
                    0,
                    new CountingDelegatingHandler(_counter));
            };
        }
    }

    private sealed class CountingDelegatingHandler : DelegatingHandler
    {
        private readonly OutboundRequestCounter _counter;

        public CountingDelegatingHandler(
            OutboundRequestCounter counter)
        {
            _counter = counter;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _counter.Record();
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CountingTerminalHandler : HttpMessageHandler
    {
        private readonly OutboundRequestCounter _counter;

        public CountingTerminalHandler(
            OutboundRequestCounter counter)
        {
            _counter = counter;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _counter.Record();
            return Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.ServiceUnavailable));
        }
    }
}
