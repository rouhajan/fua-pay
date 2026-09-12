using System.Net;
using System.Text.RegularExpressions;

using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        var summary = GetValidationSummary(content);
        Assert.Contains(
            "validation-summary-valid",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-valmsg-summary=\"true\"",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ul></ul>",
            summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<li",
            summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "style=",
            summary,
            StringComparison.OrdinalIgnoreCase);

        using var stylesheetResponse =
            await client.GetAsync("/css/base.css");
        var stylesheet =
            await stylesheetResponse.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.OK,
            stylesheetResponse.StatusCode);
        Assert.Matches(
            @"\.validation-summary-valid\s*\{[^}]*display:\s*none;",
            stylesheet);
    }

    [Fact]
    public async Task PostSignIn_WithInvalidModelState_RendersEncodedCspSafeSummary()
    {
        const string unsafeError =
            "<img src=x onerror=alert(1)>";
        using var configuredFactory =
            _factory.WithWebHostBuilder(
                builder => builder.ConfigureTestServices(
                    services => services.Configure<MvcOptions>(
                        options => options.Filters.Add(
                            new ModelStateErrorPageFilter(
                                unsafeError)))));
        using var client = configuredFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
        using var getResponse =
            await client.GetAsync("/Development/SignIn");
        var getContent =
            await getResponse.Content.ReadAsStringAsync();
        var requestVerificationToken =
            GetRequestVerificationToken(getContent);
        using var form = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["ProfileKey"] = "unsupported-profile",
                ["__RequestVerificationToken"] =
                    requestVerificationToken
            });

        using var response = await client.PostAsync(
            "/Development/SignIn?handler=SignIn",
            form);
        var content = await response.Content.ReadAsStringAsync();
        var summary = GetValidationSummary(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "validation-summary-errors",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-valmsg-summary=\"true\"",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "<ul>",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "<li>Development profile is not supported.</li>",
            summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "&lt;img src=x onerror=alert(1)&gt;",
            summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            unsafeError,
            summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "style=",
            summary,
            StringComparison.OrdinalIgnoreCase);
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

    private static string GetValidationSummary(string content)
    {
        var match = Regex.Match(
            content,
            "<div(?=[^>]*data-valmsg-summary=\"true\")[^>]*>.*?</div>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(match.Success);
        return match.Value;
    }

    private static string GetRequestVerificationToken(string content)
    {
        var match = Regex.Match(
            content,
            "<input(?=[^>]*name=\"__RequestVerificationToken\")" +
            "(?=[^>]*value=\"([^\"]+)\")[^>]*>",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed class ModelStateErrorPageFilter : IPageFilter
    {
        private readonly string _error;

        public ModelStateErrorPageFilter(string error)
        {
            _error = error;
        }

        public void OnPageHandlerSelected(
            PageHandlerSelectedContext context)
        {
        }

        public void OnPageHandlerExecuting(
            PageHandlerExecutingContext context)
        {
            if (string.Equals(
                    context.HttpContext.Request.Method,
                    "POST",
                    StringComparison.Ordinal))
            {
                context.ModelState.AddModelError(
                    "InjectedTestError",
                    _error);
            }
        }

        public void OnPageHandlerExecuted(
            PageHandlerExecutedContext context)
        {
        }
    }

}
