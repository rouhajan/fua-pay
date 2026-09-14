using System.Security.Cryptography;
using System.Text;

using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class FuaPrintAuthenticationRegistrationTests
{
    [Fact]
    public async Task Registration_UsesDedicatedSchemeAndPolicy()
    {
        var configuration = CreateConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddAuthentication(
                CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie()
            .AddFuaPrintAuthentication(configuration);
        services.AddAuthorization(
            options => options.AddFuaPrintPolicy());

        using var provider = services.BuildServiceProvider();
        var scheme = await provider
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(
                FuaPrintAuthenticationDefaults.AuthenticationScheme);
        var policy = await provider
            .GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(
                FuaPrintAuthenticationDefaults.AuthorizationPolicy);

        Assert.NotNull(scheme);
        Assert.NotNull(policy);
        Assert.Equal(
            [FuaPrintAuthenticationDefaults.AuthenticationScheme],
            policy.AuthenticationSchemes);
        Assert.Contains(
            policy.Requirements,
            requirement =>
                requirement is ClaimsAuthorizationRequirement
                {
                    ClaimType: FuaPrintAuthenticationDefaults
                        .PrintSourceIdClaim
                });
    }

    private static PrintPaymentsConfiguration CreateConfiguration()
    {
        const string credential =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";
        var digest = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.ASCII.GetBytes(credential)))
            .ToLowerInvariant();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PrintPayments:Enabled"] = "true",
                    ["PrintPayments:Sources:0:PrintSourceId"] =
                        Guid.NewGuid().ToString("D"),
                    ["PrintPayments:Sources:0:CredentialSha256"] =
                        digest
                })
            .Build();

        return PrintPaymentsConfiguration.Resolve(configuration);
    }
}
