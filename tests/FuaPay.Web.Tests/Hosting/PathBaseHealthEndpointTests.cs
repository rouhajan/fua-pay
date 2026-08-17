using System.Net;

using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Mvc.Testing;

namespace FuaPay.Web.Tests.Hosting;

public sealed class PathBaseHealthEndpointTests
{
    [Fact]
    public async Task ProtectedPage_WithPathBase_RedirectsInsidePrefix()
    {
        using var pathBaseFactory =
            new ConfiguredWebApplicationFactory(
                "Staging",
                new Dictionary<string, string?>
                {
                    ["Hosting:PathBase"] =
                        "/fuapay",
                    ["StagingTestMode:Enabled"] =
                        "true",
                    ["StagingTestMode:InteractiveSignInEnabled"] =
                        "true"
                });

        using var client = pathBaseFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        using var response =
            await client.GetAsync(
                "/fuapay/Customer/Jobs");

        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);

        var location =
            Assert.IsType<Uri>(response.Headers.Location);

        Assert.Contains(
            "/fuapay/Development/SignIn",
            location.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveProbe_WithConfiguredPathBase_UsesOnlyPrefix()
    {
        using var pathBaseFactory =
            new ConfiguredWebApplicationFactory(
                "Development",
                new Dictionary<string, string?>
                {
                    ["Hosting:PathBase"] =
                        "/fuapay"
                });

        using var client = pathBaseFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        using var prefixedResponse =
            await client.GetAsync(
                "/fuapay/health/live");
        using var unprefixedResponse =
            await client.GetAsync(
                "/health/live");

        Assert.Equal(
            HttpStatusCode.OK,
            prefixedResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            unprefixedResponse.StatusCode);
    }
}
