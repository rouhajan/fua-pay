using System.Net;

using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Mvc.Testing;

namespace FuaPay.Web.Tests;

public sealed class HomePageTests :
    IClassFixture<ConfiguredWebApplicationFactory>
{
    private readonly ConfiguredWebApplicationFactory _factory;

    public HomePageTests(ConfiguredWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRoot_InDevelopment_ReturnsPublicEntryPage()
    {
        using var client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("href=\"/Development/SignIn\"", content);
        Assert.Contains("href=\"/Privacy\"", content);
        Assert.Contains("href=\"/Terms\"", content);
        Assert.DoesNotContain("type=\"email\"", content);
        Assert.DoesNotContain("type=\"password\"", content);
    }
}
