using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Domain;

public sealed class ExternalIdentityKeyTests
{
    [Fact]
    public void Constructor_TrimsAllComponents()
    {
        var identity = new ExternalIdentityKey(
            "  entra-id  ",
            "  tul-tenant  ",
            "  subject-123  ");

        Assert.Equal(
            "entra-id",
            identity.Provider);

        Assert.Equal(
            "tul-tenant",
            identity.Tenant);

        Assert.Equal(
            "subject-123",
            identity.Subject);
    }

    [Fact]
    public void Constructor_CanonicalizesProviderCasing()
    {
        var first = new ExternalIdentityKey(
            "Microsoft-Entra",
            "tenant",
            "subject");

        var second = new ExternalIdentityKey(
            "microsoft-entra",
            "tenant",
            "subject");

        Assert.Equal("microsoft-entra", first.Provider);
        Assert.Equal(first, second);
    }

    [Fact]
    public void FromGuidIdentifiers_CanonicalizesEquivalentFormats()
    {
        var tenantId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();

        var first = ExternalIdentityKey.FromGuidIdentifiers(
            "Microsoft-Entra",
            tenantId.ToString("B").ToUpperInvariant(),
            subjectId.ToString("N").ToUpperInvariant());

        var second = ExternalIdentityKey.FromGuidIdentifiers(
            "microsoft-entra",
            tenantId.ToString("D"),
            subjectId.ToString("D"));

        Assert.Equal(first, second);
        Assert.Equal(tenantId.ToString("D"), first.Tenant);
        Assert.Equal(subjectId.ToString("D"), first.Subject);
    }

    [Theory]
    [InlineData("not-a-guid", "00000000-0000-0000-0000-000000000001")]
    [InlineData("00000000-0000-0000-0000-000000000001", "not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000001")]
    [InlineData("00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000000")]
    public void FromGuidIdentifiers_RejectsInvalidOrEmptyGuid(
        string tenant,
        string subject)
    {
        Action action = () =>
            _ = ExternalIdentityKey.FromGuidIdentifiers(
                "microsoft-entra",
                tenant,
                subject);

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData("entra id")]
    [InlineData("microsoft/entra")]
    [InlineData("český-poskytovatel")]
    public void Constructor_RejectsNonCanonicalProvider(
        string provider)
    {
        Action action = () =>
            _ = new ExternalIdentityKey(
                provider,
                "tenant",
                "subject");

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_PreservesOpaqueTenantAndSubjectCasing()
    {
        var lower = new ExternalIdentityKey(
            "provider",
            "tenant-a",
            "subject-a");

        var upper = new ExternalIdentityKey(
            "provider",
            "Tenant-A",
            "Subject-A");

        Assert.NotEqual(lower, upper);
        Assert.Equal("Tenant-A", upper.Tenant);
        Assert.Equal("Subject-A", upper.Subject);
    }

    [Theory]
    [InlineData("", "tenant", "subject")]
    [InlineData("provider", "", "subject")]
    [InlineData("provider", "tenant", "")]
    [InlineData(" ", "tenant", "subject")]
    [InlineData("provider", " ", "subject")]
    [InlineData("provider", "tenant", " ")]
    public void Constructor_RejectsBlankComponents(
        string provider,
        string tenant,
        string subject)
    {
        Action action = () =>
            _ = new ExternalIdentityKey(
                provider,
                tenant,
                subject);

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData("provider\u0000", "tenant", "subject")]
    [InlineData("provider", "tenant\u200B", "subject")]
    [InlineData("provider", "tenant", "subject\u2028")]
    [InlineData("provider", "tenant\u2028part", "subject")]
    [InlineData("provider", "tenant", "subject\u2029part")]
    public void Constructor_RejectsNonPrintableComponents(
        string provider,
        string tenant,
        string subject)
    {
        Action action = () =>
            _ = new ExternalIdentityKey(
                provider,
                tenant,
                subject);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsOverlongComponents()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExternalIdentityKey(
                    new string('p', 65),
                    "tenant",
                    "subject"));

        Assert.Throws<ArgumentException>(
            () =>
                new ExternalIdentityKey(
                    "provider",
                    new string('t', 129),
                    "subject"));

        Assert.Throws<ArgumentException>(
            () =>
                new ExternalIdentityKey(
                    "provider",
                    "tenant",
                    new string('s', 257)));
    }

    [Fact]
    public void EqualComponents_CreateEqualKeys()
    {
        var first = new ExternalIdentityKey(
            "entra-id",
            "tenant",
            "subject");

        var second = new ExternalIdentityKey(
            "entra-id",
            "tenant",
            "subject");

        Assert.Equal(first, second);
    }
}
