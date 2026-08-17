using System.Net;

using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Tests;

public sealed class DevelopmentSignInPageTests :
    IClassFixture<ConfiguredWebApplicationFactory>
{
    private readonly ConfiguredWebApplicationFactory _factory;

    public DevelopmentSignInPageTests(
        ConfiguredWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSignIn_InDevelopment_ReturnsSuccess()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        using var response =
            await client.GetAsync(
                "/Development/SignIn");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var decodedContent = WebUtility.HtmlDecode(content);

        Assert.Contains(
            "Zákazníci",
            decodedContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "Zadavatelé",
            decodedContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "Administrace",
            decodedContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "customer.alpha@example.invalid",
            decodedContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "administrator@example.invalid",
            decodedContent,
            StringComparison.Ordinal);
        Assert.Equal(
            9,
            decodedContent.Split(
                "name=\"ProfileKey\"",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task GetSignIn_InDevelopmentWhenDisabled_ReturnsNotFound()
    {
        using var disabledFactory =
            new ConfiguredWebApplicationFactory(
                Environments.Development,
                new Dictionary<string, string?>
                {
                    ["DevelopmentSignIn:Enabled"] = "false",
                    ["ConnectionStrings:FuaPay"] =
                        "Host=localhost;Database=unused;" +
                        "Username=unused;Password=unused"
                });

        using var client = disabledFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        using var response =
            await client.GetAsync(
                "/Development/SignIn");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    [Fact]
    public async Task GetSignIn_InStagingWithExplicitTestMode_ReturnsSuccess()
    {
        using var stagingFactory =
            new ConfiguredWebApplicationFactory(
                "Staging",
                new Dictionary<string, string?>
                {
                    ["StagingTestMode:Enabled"] =
                        "true",
                    ["StagingTestMode:InteractiveSignInEnabled"] =
                        "true",
                    ["ConnectionStrings:FuaPay"] =
                        "Host=localhost;" +
                        "Database=unused;" +
                        "Username=unused;" +
                        "Password=unused"
                });

        using var client = stagingFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        using var response =
            await client.GetAsync(
                "/Development/SignIn");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }

    [Fact]
    public async Task GetSignIn_OutsideDevelopment_ReturnsNotFound()
    {
        using var stagingFactory =
            new ConfiguredWebApplicationFactory(
                "Staging",
                new Dictionary<string, string?>());

        using var client = stagingFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        using var response =
            await client.GetAsync(
                "/Development/SignIn");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

}
