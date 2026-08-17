using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Development;

public sealed class DevelopmentIdentityProfilesTests
{
    [Fact]
    public void All_HasUniqueKeysAndExternalIdentities()
    {
        var profiles = DevelopmentIdentityProfiles.All;

        Assert.Equal(9, profiles.Count);

        Assert.Equal(
            profiles.Count,
            profiles
                .Select(profile => profile.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.Equal(
            profiles.Count,
            profiles
                .Select(
                    profile =>
                        (
                            profile.Identity.Key.Provider,
                            profile.Identity.Key.Tenant,
                            profile.Identity.Key.Subject
                        ))
                .Distinct()
                .Count());

        Assert.All(
            profiles,
            profile =>
            {
                Assert.Contains(AccessRole.Customer, profile.Roles);
                Assert.Equal(
                    profile.Roles.Count,
                    profile.Roles.Distinct().Count());
            });
    }

    [Fact]
    public void Sections_GroupAllProfilesExactlyOnce()
    {
        var sections = DevelopmentIdentityProfiles.Sections;

        Assert.Equal(3, sections.Count);
        Assert.Equal(
            new[] { 2, 6, 1 },
            sections.Select(section => section.Profiles.Count));

        Assert.Equal(
            DevelopmentIdentityProfiles.All
                .Select(profile => profile.Key)
                .OrderBy(key => key),
            sections
                .SelectMany(section => section.Profiles)
                .Select(profile => profile.Key)
                .OrderBy(key => key));
    }

    [Fact]
    public void All_UsesClearlyFictitiousTestAccounts()
    {
        Assert.Equal(
            new (string DisplayName, string? Email)[]
            {
                (
                    "Testovací zákazník Alfa",
                    "customer.alpha@example.invalid"),
                (
                    "Testovací zákazník Beta",
                    "customer.beta@example.invalid"),
                (
                    "Testovací zadavatel 3D tisku",
                    "requester.3d-print@example.invalid"),
                (
                    "Testovací zadavatel dílny",
                    "requester.workshop@example.invalid"),
                (
                    "Testovací zadavatel plotru",
                    "requester.plotter@example.invalid"),
                (
                    "Testovací zadavatel sekretariátu A",
                    "requester.secretariat-a@example.invalid"),
                (
                    "Testovací zadavatel sekretariátu B",
                    "requester.secretariat-b@example.invalid"),
                (
                    "Testovací zadavatel sekretariátu C",
                    "requester.secretariat-c@example.invalid"),
                ("Testovací administrátor", "administrator@example.invalid")
            },
            DevelopmentIdentityProfiles.All
                .Select(
                    profile =>
                        (
                            profile.DisplayName,
                            profile.Identity.Email
                        )));
    }

    [Fact]
    public void RequestersAndAdministrator_HaveExpectedRoles()
    {
        var admin =
            Assert.IsType<DevelopmentIdentityProfile>(
                DevelopmentIdentityProfiles.Find(
                    DevelopmentIdentityProfiles.AdministratorKey));

        Assert.All(
            DevelopmentIdentityProfiles.All.Where(
                profile =>
                    profile.Group ==
                    DevelopmentIdentityProfileGroup.Requester),
            profile =>
                Assert.Equal(
                    new[]
                    {
                        AccessRole.Customer,
                        AccessRole.Requester
                    },
                    profile.Roles));

        Assert.Equal(
            new[]
            {
                AccessRole.Customer,
                AccessRole.Admin
            },
            admin.Roles);
    }
}
