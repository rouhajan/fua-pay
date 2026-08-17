using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Tests.Modules.Access.Application;

public sealed class VerifiedExternalIdentityTests
{
    [Fact]
    public void Constructor_TrimsProfileValues()
    {
        var identity =
            new VerifiedExternalIdentity(
                CreateKey(),
                "  Testovací uživatel  ",
                "  user@example.invalid  ");

        Assert.Equal(
            "Testovací uživatel",
            identity.DisplayName);

        Assert.Equal(
            "user@example.invalid",
            identity.Email);
    }

    [Fact]
    public void Constructor_AllowsMissingEmail()
    {
        var identity =
            new VerifiedExternalIdentity(
                CreateKey(),
                "Uživatel bez e-mailu",
                null);

        Assert.Null(identity.Email);
    }

    [Fact]
    public void Constructor_RejectsOverlongProfileValues()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new VerifiedExternalIdentity(
                    CreateKey(),
                    new string('A', 257),
                    null));

        Assert.Throws<ArgumentException>(
            () =>
                new VerifiedExternalIdentity(
                    CreateKey(),
                    "Uživatel",
                    new string('a', 321)));
    }

    [Theory]
    [InlineData(" ", "user@example.cz")]
    [InlineData("Uživatel", " ")]
    public void Constructor_RejectsBlankProfileValues(
        string displayName,
        string email)
    {
        Action action = () =>
            _ = new VerifiedExternalIdentity(
                CreateKey(),
                displayName,
                email);

        Assert.Throws<ArgumentException>(action);
    }

    private static ExternalIdentityKey CreateKey()
    {
        return new ExternalIdentityKey(
            "entra-id",
            "tul-tenant",
            "subject-123");
    }
}
