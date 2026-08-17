using FuaPay.Web.Modules.Access.Infrastructure.Entra;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FuaPay.Web.Tests.Modules.Access.Infrastructure.Entra;

public sealed class EntraAuthenticationConfigurationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    [Fact]
    public void Resolve_ProductionWithoutEntra_FailsClosed()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>());

        Assert.Throws<InvalidOperationException>(
            () => EntraAuthenticationConfiguration.Resolve(
                configuration,
                Environments.Production,
                interactiveTestSignInEnabled: false));
    }

    [Fact]
    public void Resolve_EnabledConfiguration_UsesTenantSpecificAuthority()
    {
        var configuration = ValidConfiguration();

        var result = EntraAuthenticationConfiguration.Resolve(
            configuration,
            Environments.Production,
            interactiveTestSignInEnabled: false);

        Assert.True(result.Enabled);
        Assert.Equal(TenantId, result.TenantId);
        Assert.Equal(ClientId, result.ClientId);
        Assert.Equal(
            "test-secret-not-for-production",
            result.ClientSecret);
        Assert.Equal(
            $"https://login.microsoftonline.com/{TenantId:D}/v2.0",
            result.Authority?.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("/signin-oidc", result.CallbackPath.Value);
        Assert.Equal(
            "/signout-callback-oidc",
            result.SignedOutCallbackPath.Value);
    }

    [Fact]
    public void Resolve_ClientSecret_IsNotNormalized()
    {
        var values = ValidValues();
        values["Entra:ClientSecret"] = " secret-with-spaces ";

        var result = EntraAuthenticationConfiguration.Resolve(
            Configuration(values),
            Environments.Staging,
            interactiveTestSignInEnabled: false);

        Assert.Equal(" secret-with-spaces ", result.ClientSecret);
    }

    [Fact]
    public void Resolve_EntraAndTestSignInTogether_IsRejected()
    {
        var configuration = ValidConfiguration();

        Assert.Throws<InvalidOperationException>(
            () => EntraAuthenticationConfiguration.Resolve(
                configuration,
                Environments.Development,
                interactiveTestSignInEnabled: true));
    }

    [Theory]
    [InlineData("Entra:TenantId")]
    [InlineData("Entra:ClientId")]
    [InlineData("Entra:ClientSecret")]
    public void Resolve_MissingRequiredValue_IsRejected(
        string missingKey)
    {
        var values = ValidValues();
        values[missingKey] = string.Empty;

        Assert.Throws<InvalidOperationException>(
            () => EntraAuthenticationConfiguration.Resolve(
                Configuration(values),
                Environments.Staging,
                interactiveTestSignInEnabled: false));
    }

    private static IConfiguration ValidConfiguration() =>
        Configuration(ValidValues());

    private static Dictionary<string, string?> ValidValues() =>
        new()
        {
            ["Entra:Enabled"] = "true",
            ["Entra:TenantId"] = TenantId.ToString("D"),
            ["Entra:ClientId"] = ClientId.ToString("D"),
            ["Entra:ClientSecret"] = "test-secret-not-for-production"
        };

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
