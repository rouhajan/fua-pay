using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Credits.Application;

public sealed class PrintCredentialServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CustomerCanConfigureChangeAndRevokeOwnCredentialWithoutSecretLeak()
    {
        var repository = new FakeRepository();
        var audit = new RecordingAuditTrail();
        var hasher = CreateHasher(1);
        var service = CreateService(repository, audit, hasher, Customer());

        await service.SetAsync(OwnerId, "123456", "123456");

        var credential = Assert.Single(repository.Credentials);
        Assert.Equal("student@tul.cz", credential.NormalizedEmail);
        Assert.DoesNotContain("123456", credential.CodeHash, StringComparison.Ordinal);
        Assert.True(hasher.Verify(credential.CodeHash, "123456"));
        Assert.DoesNotContain(
            audit.Entries,
            entry => entry.Description.Contains("123456", StringComparison.Ordinal));

        await service.SetAsync(OwnerId, "654321", "654321");

        Assert.False(hasher.Verify(credential.CodeHash, "123456"));
        Assert.True(hasher.Verify(credential.CodeHash, "654321"));
        Assert.Contains(audit.Entries, entry => entry.Action == "print-credential.changed");

        await service.RevokeAsync(OwnerId);

        Assert.False(credential.IsActive);
        Assert.Contains(audit.Entries, entry => entry.Action == "print-credential.revoked");
    }

    [Theory]
    [InlineData("12345", "12345")]
    [InlineData("12345a", "12345a")]
    [InlineData("123456", "654321")]
    public async Task InvalidOrUnconfirmedCodeIsRejected(string code, string confirmation)
    {
        var repository = new FakeRepository();
        var service = CreateService(
            repository,
            new RecordingAuditTrail(),
            CreateHasher(1),
            Customer());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SetAsync(OwnerId, code, confirmation));
        Assert.Empty(repository.Credentials);
    }

    [Fact]
    public async Task NonCustomerCannotConfigureCredential()
    {
        var service = CreateService(
            new FakeRepository(),
            new RecordingAuditTrail(),
            CreateHasher(1),
            Customer() with { Roles = [] });

        await Assert.ThrowsAsync<PrintCredentialUnavailableException>(
            () => service.SetAsync(OwnerId, "123456", "123456"));
    }

    [Fact]
    public async Task AmbiguousTrustedEmailFailsClosed()
    {
        var repository = new FakeRepository { MatchingAccessUserCount = 2 };
        var service = CreateService(
            repository,
            new RecordingAuditTrail(),
            CreateHasher(1),
            Customer());

        await Assert.ThrowsAsync<PrintCredentialUnavailableException>(
            () => service.SetAsync(OwnerId, "123456", "123456"));
        Assert.Empty(repository.Credentials);
    }

    [Fact]
    public void CodesArePepperedAndMayBeSharedByDifferentOwners()
    {
        var first = CreateHasher(1);
        var second = CreateHasher(2);
        var firstHash = first.Hash("123456");
        var anotherHash = first.Hash("123456");

        Assert.NotEqual(firstHash, anotherHash);
        Assert.True(first.Verify(firstHash, "123456"));
        Assert.False(second.Verify(firstHash, "123456"));
    }

    [Theory]
    [InlineData(" Student@TUL.CZ ", "student@tul.cz")]
    [InlineData("student@tul.cz", "student@tul.cz")]
    public void EmailNormalizationFoldsAsciiCaseAndTrimsAsciiSpace(
        string value,
        string expected)
    {
        Assert.Equal(expected, PrintCredentialEmail.Normalize(value));
    }

    [Theory]
    [InlineData("student@tül.cz", "student@tül.cz")]
    [InlineData("ｓtudent@tul.cz", "student@tul.cz")]
    public void EmailNormalizationPreservesUnicodeAndAppliesCompatibilityNormalization(
        string value,
        string expected)
    {
        Assert.Equal(expected, PrintCredentialEmail.Normalize(value));
    }

    [Fact]
    public void EmailNormalizationPreservesNonAsciiCase()
    {
        Assert.Equal(
            "Žluťoučký@tul.cz",
            PrintCredentialEmail.Normalize("Žluťoučký@TUL.CZ"));
        Assert.NotEqual(
            PrintCredentialEmail.Normalize("Žluťoučký@tul.cz"),
            PrintCredentialEmail.Normalize("žluťoučký@tul.cz"));
    }

    [Fact]
    public async Task TrustedEmailChangeLeavesOldCredentialUnconfigured()
    {
        var repository = new FakeRepository();
        var audit = new RecordingAuditTrail();
        var hasher = CreateHasher(1);
        await CreateService(repository, audit, hasher, Customer())
            .SetAsync(OwnerId, "123456", "123456");

        var changedIdentity = Customer() with
        {
            Email = "changed@tul.cz"
        };
        var view = await CreateService(
            repository,
            audit,
            hasher,
            changedIdentity).GetAsync(OwnerId);

        Assert.Equal("changed@tul.cz", view.Email);
        Assert.False(view.IsConfigured);
    }

    [Fact]
    public async Task ConcurrentInitialSetIsRetriedWithoutRawPersistenceFailure()
    {
        var repository = new FakeRepository();
        repository.SaveFailures.Enqueue(
            new PrintCredentialConcurrencyException(
                new InvalidOperationException("simulated primary-key race")));
        var service = CreateService(
            repository,
            new RecordingAuditTrail(),
            CreateHasher(1),
            Customer());

        await service.SetAsync(OwnerId, "123456", "123456");

        Assert.Equal(2, repository.SaveCalls);
        Assert.Single(repository.Credentials);
    }

    [Fact]
    public async Task ActiveEmailUniqueConflictBecomesSafeUnavailableResult()
    {
        var repository = new FakeRepository();
        repository.SaveFailures.Enqueue(
            new PrintCredentialEmailConflictException(
                new InvalidOperationException("simulated unique conflict")));
        var service = CreateService(
            repository,
            new RecordingAuditTrail(),
            CreateHasher(1),
            Customer());

        await Assert.ThrowsAsync<PrintCredentialUnavailableException>(
            () => service.SetAsync(OwnerId, "123456", "123456"));
    }

    private static PrintCredentialService CreateService(
        FakeRepository repository,
        RecordingAuditTrail audit,
        IPrintCodeHasher hasher,
        AccessSessionSnapshot snapshot)
    {
        return new PrintCredentialService(
            new FakeAccessQueries(snapshot),
            repository,
            hasher,
            new ImmediateTransaction(),
            audit,
            new FixedTimeProvider(Now));
    }

    private static AccessSessionSnapshot Customer() =>
        new(
            OwnerId,
            "Student",
            " Student@TUL.CZ ",
            AccessUserStatus.Active,
            [AccessRole.Customer]);

    private static IPrintCodeHasher CreateHasher(byte fill)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PrintCredentials:Enabled"] = "true",
                ["PrintCredentials:PepperBase64"] =
                    Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray())
            })
            .Build();
        return new PrintCodeHasher(
            PrintCredentialSecurityConfiguration.Resolve(
                configuration,
                printPaymentsEnabled: true));
    }

    private sealed class FakeAccessQueries(AccessSessionSnapshot snapshot) : IAccessSessionQueries
    {
        public Task<AccessSessionSnapshot?> FindAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AccessSessionSnapshot?>(
                userId == snapshot.UserId ? snapshot : null);
    }

    private sealed class FakeRepository : IPrintCredentialRepository
    {
        public List<PrintCredential> Credentials { get; } = [];

        public long MatchingAccessUserCount { get; set; } = 1;

        public Queue<Exception> SaveFailures { get; } = new();

        public int SaveCalls { get; private set; }

        public Task<PrintCredential?> FindByOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Credentials.SingleOrDefault(item => item.OwnerId == ownerId));

        public Task<PrintCredentialAuthenticationCandidate?> FindAuthenticationCandidateAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintCredentialAuthenticationCandidate?>(null);

        public Task<long> CountAccessUsersByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MatchingAccessUserCount);

        public void Add(PrintCredential credential) => Credentials.Add(credential);

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (SaveFailures.TryDequeue(out var exception))
            {
                return Task.FromException(exception);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateTransaction : IApplicationTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
