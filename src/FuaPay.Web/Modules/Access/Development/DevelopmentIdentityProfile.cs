using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Development;

public enum DevelopmentIdentityProfileGroup
{
    Customer = 1,
    Requester = 2,
    Administrator = 3
}

public sealed record DevelopmentIdentityProfile(
    string Key,
    DevelopmentIdentityProfileGroup Group,
    string DisplayName,
    string Description,
    VerifiedExternalIdentity Identity,
    IReadOnlyCollection<AccessRole> Roles);

public sealed record DevelopmentIdentityProfileSection(
    DevelopmentIdentityProfileGroup Group,
    string Title,
    string Description,
    IReadOnlyList<DevelopmentIdentityProfile> Profiles);

public static class DevelopmentIdentityProfiles
{
    public const string Provider = "development";
    public const string Tenant = "fua-pay-local";

    public const string PrimaryCustomerKey =
        "customer-primary";

    public const string LowCreditCustomerKey =
        "customer-low-credit";

    public const string ThreeDPrintRequesterKey =
        "requester-3d-print";

    public const string WorkshopRequesterKey =
        "requester-workshop";

    public const string PlotterRequesterKey =
        "requester-plotter";

    public const string SecretariatRequesterAKey =
        "requester-secretariat-a";

    public const string SecretariatRequesterBKey =
        "requester-secretariat-b";

    public const string SecretariatRequesterCKey =
        "requester-secretariat-c";

    public const string AdministratorKey =
        "administrator";

    private static readonly IReadOnlyList<DevelopmentIdentityProfile>
        Profiles =
        [
            Create(
                PrimaryCustomerKey,
                DevelopmentIdentityProfileGroup.Customer,
                "Testovací zákazník Alfa",
                "Zákaznický účet pro kontrolu kreditu, zakázek a plateb.",
                "customer.alpha@example.invalid",
                AccessRole.Customer),
            Create(
                LowCreditCustomerKey,
                DevelopmentIdentityProfileGroup.Customer,
                "Testovací zákazník Beta",
                "Zákaznický účet s nízkým kreditem pro ověření různých způsobů úhrady.",
                "customer.beta@example.invalid",
                AccessRole.Customer),
            CreateRequester(
                ThreeDPrintRequesterKey,
                "Testovací zadavatel 3D tisku",
                "3D tisk",
                "requester.3d-print@example.invalid"),
            CreateRequester(
                WorkshopRequesterKey,
                "Testovací zadavatel dílny",
                "Dílna",
                "requester.workshop@example.invalid"),
            CreateRequester(
                PlotterRequesterKey,
                "Testovací zadavatel plotru",
                "Plotr",
                "requester.plotter@example.invalid"),
            CreateRequester(
                SecretariatRequesterAKey,
                "Testovací zadavatel sekretariátu A",
                "Sekretariát",
                "requester.secretariat-a@example.invalid"),
            CreateRequester(
                SecretariatRequesterBKey,
                "Testovací zadavatel sekretariátu B",
                "Sekretariát",
                "requester.secretariat-b@example.invalid"),
            CreateRequester(
                SecretariatRequesterCKey,
                "Testovací zadavatel sekretariátu C",
                "Sekretariát",
                "requester.secretariat-c@example.invalid"),
            Create(
                AdministratorKey,
                DevelopmentIdentityProfileGroup.Administrator,
                "Testovací administrátor",
                "Globální správa uživatelů, pracovišť, zakázek, plateb, kreditu a exportů.",
                "administrator@example.invalid",
                AccessRole.Customer,
                AccessRole.Admin)
        ];

    private static readonly IReadOnlyList<DevelopmentIdentityProfileSection>
        ProfileSections =
        [
            CreateSection(
                DevelopmentIdentityProfileGroup.Customer,
                "Zákazníci",
                "Kontrola vlastního kreditu, zakázek a plateb."),
            CreateSection(
                DevelopmentIdentityProfileGroup.Requester,
                "Zadavatelé",
                "Každý zadavatel vidí pouze zakázky svého pracoviště."),
            CreateSection(
                DevelopmentIdentityProfileGroup.Administrator,
                "Administrace",
                "Globální pohled určený pro správu a kontrolu aplikace.")
        ];

    public static IReadOnlyList<DevelopmentIdentityProfile> All =>
        Profiles;

    public static IReadOnlyList<DevelopmentIdentityProfileSection>
        Sections => ProfileSections;

    public static DevelopmentIdentityProfile? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return Profiles.SingleOrDefault(
            profile =>
                string.Equals(
                    profile.Key,
                    key.Trim(),
                    StringComparison.OrdinalIgnoreCase));
    }

    private static DevelopmentIdentityProfile CreateRequester(
        string key,
        string displayName,
        string serviceUnit,
        string email)
    {
        return Create(
            key,
            DevelopmentIdentityProfileGroup.Requester,
            displayName,
            $"Zadavatel pracoviště {serviceUnit}.",
            email,
            AccessRole.Customer,
            AccessRole.Requester);
    }

    private static DevelopmentIdentityProfile Create(
        string key,
        DevelopmentIdentityProfileGroup group,
        string displayName,
        string description,
        string email,
        params AccessRole[] roles)
    {
        return new DevelopmentIdentityProfile(
            key,
            group,
            displayName,
            description,
            new VerifiedExternalIdentity(
                new ExternalIdentityKey(
                    Provider,
                    Tenant,
                    key),
                displayName,
                email),
            Array.AsReadOnly(roles));
    }

    private static DevelopmentIdentityProfileSection CreateSection(
        DevelopmentIdentityProfileGroup group,
        string title,
        string description)
    {
        return new DevelopmentIdentityProfileSection(
            group,
            title,
            description,
            Array.AsReadOnly(
                Profiles
                    .Where(profile => profile.Group == group)
                    .ToArray()));
    }
}
