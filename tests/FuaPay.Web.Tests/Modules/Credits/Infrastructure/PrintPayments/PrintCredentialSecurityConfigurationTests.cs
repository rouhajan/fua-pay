using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialSecurityConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    public void Resolve_WhenRequiredRejectsMissingOrInvalidPepper(string? value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrintCredentials:PepperBase64"] = value
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => PrintCredentialSecurityConfiguration.Resolve(
                configuration,
                required: true));
    }
}
