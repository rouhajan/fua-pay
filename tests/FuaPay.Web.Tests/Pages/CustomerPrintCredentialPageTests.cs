using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;
using FuaPay.Web.Pages.Customer.PrintCredential;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Pages;

public sealed class CustomerPrintCredentialPageTests
{
    [Fact]
    public async Task DisabledFeatureReturnsNotFoundBeforeAnyCredentialOperation()
    {
        var configuration = PrintCredentialSecurityConfiguration.Resolve(
            new ConfigurationBuilder().Build(),
            printPaymentsEnabled: false);
        var page = new IndexModel(null!, configuration);

        var get = await page.OnGetAsync();
        var set = await page.OnPostSetAsync();
        var revoke = await page.OnPostRevokeAsync();

        Assert.IsType<NotFoundResult>(get);
        Assert.IsType<NotFoundResult>(set);
        Assert.IsType<NotFoundResult>(revoke);
    }
}
