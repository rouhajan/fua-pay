using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Domain;

public sealed class CreditReturnHoldTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 28, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CreditReturnHoldState.Consumed)]
    [InlineData(CreditReturnHoldState.Released)]
    public void TerminalTransition_IsIdempotent(
        CreditReturnHoldState terminalState)
    {
        var hold = CreateHold();
        var changedAt = CreatedAt.AddMinutes(1);

        var firstChanged = terminalState == CreditReturnHoldState.Consumed
            ? hold.Consume(changedAt)
            : hold.Release(changedAt);
        var replayChanged = terminalState == CreditReturnHoldState.Consumed
            ? hold.Consume(changedAt)
            : hold.Release(changedAt);

        Assert.True(firstChanged);
        Assert.False(replayChanged);
        Assert.Equal(terminalState, hold.State);
        Assert.Equal(changedAt, hold.StateChangedAt);
    }

    [Theory]
    [InlineData(CreditReturnHoldState.Consumed)]
    [InlineData(CreditReturnHoldState.Released)]
    public void DifferentTerminalTransition_IsRejected(
        CreditReturnHoldState firstState)
    {
        var hold = CreateHold();

        if (firstState == CreditReturnHoldState.Consumed)
        {
            hold.Consume(CreatedAt.AddMinutes(1));
        }
        else
        {
            hold.Release(CreatedAt.AddMinutes(1));
        }

        Assert.Throws<InvalidCreditReturnHoldStateTransitionException>(
            () =>
            {
                if (firstState == CreditReturnHoldState.Consumed)
                {
                    hold.Release(CreatedAt.AddMinutes(2));
                }
                else
                {
                    hold.Consume(CreatedAt.AddMinutes(2));
                }
            });
    }

    [Fact]
    public void Transition_WhenTimeMovesBackward_IsRejected()
    {
        var hold = CreateHold();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => hold.Release(CreatedAt.AddTicks(-1)));
    }

    [Fact]
    public void Constructor_WhenAmountIsNotPositive_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CreditReturnHold(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Money.Zero,
                CreatedAt));
    }

    private static CreditReturnHold CreateHold() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(500),
            CreatedAt);
}
