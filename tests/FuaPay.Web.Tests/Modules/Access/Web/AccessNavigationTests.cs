using FuaPay.Web.Modules.Access.Web;

namespace FuaPay.Web.Tests.Modules.Access.Web;

public sealed class AccessNavigationTests
{
    [Fact]
    public void For_Customer_ReturnsCustomerNavigation()
    {
        var items = AccessNavigation.For(
            AccessView.Customer,
            printCredentialsEnabled: true);

        Assert.Equal(
            new[]
            {
                "Přehled",
                "Kredit",
                "Tiskový kód",
                "Platby",
                "Zakázky",
                "Nápověda"
            },
            items.Select(item => item.Label));

        Assert.Single(items, item => item.IsOverview);
    }

    [Fact]
    public void For_CustomerWhenPrintCredentialsDisabledOmitsCredentialPage()
    {
        var items = AccessNavigation.For(AccessView.Customer);

        Assert.DoesNotContain(
            items,
            item => item.Page == "/Customer/PrintCredential/Index");
    }

    [Fact]
    public void For_Requester_ReturnsRequesterNavigation()
    {
        var items = AccessNavigation.For(AccessView.Requester);

        Assert.Equal(
            new[]
            {
                "Přehled",
                "Nová zakázka",
                "Zakázky",
                "Nápověda"
            },
            items.Select(item => item.Label));
    }

    [Fact]
    public void For_Admin_ReturnsAdministrationNavigation()
    {
        var items = AccessNavigation.For(AccessView.Admin);

        Assert.Equal(
            new[]
            {
                "Přehled",
                "Uživatelé a role",
                "Pracoviště",
                "Zakázky",
                "Platby",
                "Kredit",
                "Exporty",
                "Audit",
                "Oznámení",
                "Nápověda"
            },
            items.Select(item => item.Label));
    }

    [Theory]
    [InlineData("/Customer/Payments/CreateTopUp", "Kredit")]
    [InlineData("/Customer/Payments/Index", "Platby")]
    [InlineData("/Customer/Payments/Details", "Platby")]
    [InlineData("/Customer/PrintCredential/Index", "Tiskový kód")]
    public void FindActive_CustomerPaymentPages_SelectExpectedItem(
        string currentPage,
        string expectedLabel)
    {
        var active = AccessNavigation.FindActive(
            AccessView.Customer,
            currentPage,
            printCredentialsEnabled:
                currentPage.StartsWith(
                    "/Customer/PrintCredential",
                    StringComparison.Ordinal));

        Assert.NotNull(active);
        Assert.Equal(expectedLabel, active.Label);
    }
}
