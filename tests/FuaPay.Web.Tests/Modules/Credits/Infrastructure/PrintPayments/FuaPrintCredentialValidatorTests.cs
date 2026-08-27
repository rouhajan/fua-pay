using System.Security.Cryptography;
using System.Text;

using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class FuaPrintCredentialValidatorTests
{
    private const string Credential =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";

    [Fact]
    public void TryValidate_ValidBearerCredentialReturnsServerSideSource()
    {
        var sourceId = Guid.NewGuid();
        var validator = CreateValidator(sourceId, Credential);

        var result = validator.TryValidate(
            $"Bearer {Credential}",
            out var resolvedSourceId);

        Assert.True(result);
        Assert.Equal(sourceId, resolvedSourceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ")]
    [InlineData("Bearer too-short")]
    [InlineData("Bearer abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOP!")]
    public void TryValidate_MalformedCredentialFailsClosed(
        string? authorization)
    {
        var validator = CreateValidator(Guid.NewGuid(), Credential);

        Assert.False(
            validator.TryValidate(
                authorization,
                out var sourceId));
        Assert.Equal(Guid.Empty, sourceId);
    }

    [Fact]
    public void TryValidate_WrongCredentialFailsClosed()
    {
        var validator = CreateValidator(Guid.NewGuid(), Credential);
        var otherCredential = new string('Z', Credential.Length);

        Assert.False(
            validator.TryValidate(
                $"Bearer {otherCredential}",
                out var sourceId));
        Assert.Equal(Guid.Empty, sourceId);
    }

    [Fact]
    public void TryValidate_DisabledFeatureRejectsValidCredential()
    {
        var validator = new FuaPrintCredentialValidator(
            new PrintPaymentsConfiguration(false, []));

        Assert.False(
            validator.TryValidate(
                $"Bearer {Credential}",
                out _));
    }

    private static FuaPrintCredentialValidator CreateValidator(
        Guid sourceId,
        string credential)
    {
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
                        sourceId.ToString("D"),
                    ["PrintPayments:Sources:0:CredentialSha256"] =
                        digest
                })
            .Build();

        return new FuaPrintCredentialValidator(
            PrintPaymentsConfiguration.Resolve(configuration));
    }
}
