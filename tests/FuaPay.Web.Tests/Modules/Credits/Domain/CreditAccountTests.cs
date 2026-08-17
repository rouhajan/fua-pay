using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Domain;

public sealed class CreditAccountTests
{
    private static readonly DateTimeOffset RecordedAt =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewAccount_HasZeroBalanceAndEmptyHistory()
    {
        var account = CreateAccount();

        Assert.Equal(Money.Zero, account.Balance);
        Assert.Empty(account.Movements);
    }

    [Fact]
    public void Credit_IncreasesBalanceAndAddsMovement()
    {
        var account = CreateAccount();
        var operationId = Guid.NewGuid();

        var movement = account.Credit(
            operationId,
            new Money(12_500),
            RecordedAt,
            "Dobití kreditu");

        Assert.Equal(new Money(12_500), account.Balance);
        Assert.Equal(operationId, movement.OperationId);
        Assert.Equal(CreditMovementType.Credit, movement.Type);
        Assert.Equal(new Money(12_500), movement.BalanceAfter);
        Assert.Single(account.Movements);
    }

    [Fact]
    public void Debit_DecreasesBalanceAndAddsMovement()
    {
        var account = CreateAccount();

        account.Credit(
            Guid.NewGuid(),
            new Money(10_000),
            RecordedAt,
            "Dobití kreditu");

        var movement = account.Debit(
            Guid.NewGuid(),
            new Money(4_000),
            RecordedAt.AddMinutes(1),
            "Úhrada zakázky");

        Assert.Equal(new Money(6_000), account.Balance);
        Assert.Equal(CreditMovementType.Debit, movement.Type);
        Assert.Equal(new Money(6_000), movement.BalanceAfter);
        Assert.Equal(2, account.Movements.Count);
    }

    [Fact]
    public void Debit_RejectsAmountGreaterThanBalance()
    {
        var account = CreateAccount();

        account.Credit(
            Guid.NewGuid(),
            new Money(5_000),
            RecordedAt,
            "Dobití kreditu");

        Action action = () =>
        {
            _ = account.Debit(
                Guid.NewGuid(),
                new Money(5_001),
                RecordedAt.AddMinutes(1),
                "Úhrada zakázky");
        };

        Assert.Throws<InsufficientCreditException>(action);
        Assert.Equal(new Money(5_000), account.Balance);
        Assert.Single(account.Movements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Credit_RejectsNonPositiveAmount(long minorUnits)
    {
        var account = CreateAccount();

        Action action = () =>
        {
            _ = account.Credit(
                Guid.NewGuid(),
                new Money(minorUnits),
                RecordedAt,
                "Dobití kreditu");
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(Money.Zero, account.Balance);
        Assert.Empty(account.Movements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Debit_RejectsNonPositiveAmount(long minorUnits)
    {
        var account = CreateAccount();

        Action action = () =>
        {
            _ = account.Debit(
                Guid.NewGuid(),
                new Money(minorUnits),
                RecordedAt,
                "Úhrada zakázky");
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(Money.Zero, account.Balance);
        Assert.Empty(account.Movements);
    }

    [Fact]
    public void RepeatedOperationId_IsRejected()
    {
        var account = CreateAccount();
        var operationId = Guid.NewGuid();

        account.Credit(
            operationId,
            new Money(2_000),
            RecordedAt,
            "Dobití kreditu");

        Action action = () =>
        {
            _ = account.Credit(
                operationId,
                new Money(2_000),
                RecordedAt.AddMinutes(1),
                "Opakované dobití");
        };

        var exception =
            Assert.Throws<DuplicateCreditOperationException>(action);

        Assert.Equal(operationId, exception.OperationId);
        Assert.Equal(new Money(2_000), account.Balance);
        Assert.Single(account.Movements);
    }

    [Fact]
    public void Movements_PreserveInsertionOrder()
    {
        var account = CreateAccount();

        var first = account.Credit(
            Guid.NewGuid(),
            new Money(8_000),
            RecordedAt,
            "Dobití kreditu");

        var second = account.Debit(
            Guid.NewGuid(),
            new Money(3_000),
            RecordedAt.AddMinutes(1),
            "Úhrada zakázky");

        Assert.Collection(
            account.Movements,
            movement => Assert.Same(first, movement),
            movement => Assert.Same(second, movement));
    }

    private static CreditAccount CreateAccount()
    {
        return new CreditAccount(
            Guid.NewGuid(),
            Guid.NewGuid());
    }
}
