using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintCredentialDummyVerifierTests
{
    [Fact]
    public void EnabledConfigurationCreatesExpensiveDummyHashOnce()
    {
        var hasher = new CountingHasher();
        var verifier = new PrintCredentialDummyVerifier(
            Configuration(enabled: true),
            hasher);

        Assert.Equal("dummy-hash", verifier.Hash);
        Assert.Equal("dummy-hash", verifier.Hash);
        Assert.Equal(1, hasher.HashCalls);
    }

    [Fact]
    public void DisabledConfigurationDoesNotHash()
    {
        var hasher = new CountingHasher();
        var verifier = new PrintCredentialDummyVerifier(
            Configuration(enabled: false),
            hasher);

        Assert.Empty(verifier.Hash);
        Assert.Equal(0, hasher.HashCalls);
    }

    private static PrintCredentialSecurityConfiguration Configuration(
        bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrintCredentials:Enabled"] = enabled.ToString(),
                ["PrintCredentials:PepperBase64"] =
                    Convert.ToBase64String(new byte[32])
            })
            .Build();

        return PrintCredentialSecurityConfiguration.Resolve(
            configuration,
            printPaymentsEnabled: enabled);
    }

    private sealed class CountingHasher : IPrintCodeHasher
    {
        public int HashCalls { get; private set; }

        public string Hash(string printCode)
        {
            HashCalls++;
            return "dummy-hash";
        }

        public bool Verify(string hash, string printCode) =>
            throw new NotSupportedException();
    }
}
