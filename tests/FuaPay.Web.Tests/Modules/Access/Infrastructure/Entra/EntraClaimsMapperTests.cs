using System.Security.Claims;

using FuaPay.Web.Modules.Access.Infrastructure.Entra;

namespace FuaPay.Web.Tests.Modules.Access.Infrastructure.Entra;

public sealed class EntraClaimsMapperTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ObjectId = Guid.NewGuid();

    [Fact]
    public void CreateVerifiedIdentity_UsesTenantAndObjectIdAsStableKey()
    {
        var principal = Principal(
            new Claim("tid", TenantId.ToString("B").ToUpperInvariant()),
            new Claim("oid", ObjectId.ToString("B").ToUpperInvariant()),
            new Claim("name", "  Test User  "),
            new Claim("email", "  user@example.invalid  "),
            new Claim("roles", "GlobalAdministrator"));

        var result = EntraClaimsMapper.CreateVerifiedIdentity(
            principal,
            TenantId);

        Assert.Equal(
            EntraAuthenticationDefaults.ExternalIdentityProvider,
            result.Key.Provider);
        Assert.Equal(TenantId.ToString("D"), result.Key.Tenant);
        Assert.Equal(ObjectId.ToString("D"), result.Key.Subject);
        Assert.Equal("Test User", result.DisplayName);
        Assert.Equal("user@example.invalid", result.Email);
    }

    [Fact]
    public void CreateVerifiedIdentity_WrongTenant_IsRejected()
    {
        var principal = Principal(
            new Claim("tid", Guid.NewGuid().ToString("D")),
            new Claim("oid", ObjectId.ToString("D")));

        Assert.Throws<ArgumentException>(
            () => EntraClaimsMapper.CreateVerifiedIdentity(
                principal,
                TenantId));
    }

    [Theory]
    [InlineData("tid")]
    [InlineData("oid")]
    public void CreateVerifiedIdentity_DuplicateStableClaim_IsRejected(
        string claimType)
    {
        var claims = new List<Claim>
        {
            new("tid", TenantId.ToString("D")),
            new("oid", ObjectId.ToString("D")),
            new(claimType, Guid.NewGuid().ToString("D"))
        };

        Assert.Throws<ArgumentException>(
            () => EntraClaimsMapper.CreateVerifiedIdentity(
                Principal(claims.ToArray()),
                TenantId));
    }

    [Fact]
    public void CreateVerifiedIdentity_MissingProfileClaims_UsesObjectId()
    {
        var principal = Principal(
            new Claim("tid", TenantId.ToString("D")),
            new Claim("oid", ObjectId.ToString("D")));

        var result = EntraClaimsMapper.CreateVerifiedIdentity(
            principal,
            TenantId);

        Assert.Equal(ObjectId.ToString("D"), result.DisplayName);
        Assert.Null(result.Email);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(
            new ClaimsIdentity(
                claims,
                authenticationType: "validated-oidc"));
}
