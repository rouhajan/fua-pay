using System.Globalization;

using FuaPay.Web.Modules.Receipts.Infrastructure.Pdf;

namespace FuaPay.Web.Tests.Modules.Receipts.Infrastructure.Pdf;

public sealed class ReceiptTextLayoutTests
{
    [Fact]
    public void Wrap_LongTokenSplitsIntoLinesWithinWidth()
    {
        var lines = ReceiptTextLayout.Wrap(
            "ABCD EFGHIJKLM",
            width: 4,
            measure: value => value.Length);

        Assert.Equal(
            ["ABCD", "EFGH", "IJKL", "M"],
            lines);
        Assert.All(
            lines,
            line => Assert.InRange(line.Length, 1, 4));
    }

    [Fact]
    public void Wrap_DoesNotSplitCombiningTextElement()
    {
        const string text = "A\u0301B";

        var lines = ReceiptTextLayout.Wrap(
            text,
            width: 1,
            measure: value =>
                StringInfo.ParseCombiningCharacters(value).Length);

        Assert.Equal(["A\u0301", "B"], lines);
    }

    [Fact]
    public void Wrap_SingleTextElementWiderThanWidthFailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ReceiptTextLayout.Wrap(
                "A",
                width: 0.5,
                measure: value => value.Length));

        Assert.Contains(
            "širší než dostupný prostor",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FitsBeforeFooter_ExactBoundaryFits()
    {
        var result = ReceiptTextLayout.FitsBeforeFooter(
            contentY: 100,
            contentHeight: 18,
            contentGap: 12,
            footerY: 130);

        Assert.True(result);
    }

    [Fact]
    public void FitsBeforeFooter_ContentCollisionDoesNotFit()
    {
        var result = ReceiptTextLayout.FitsBeforeFooter(
            contentY: 101,
            contentHeight: 18,
            contentGap: 12,
            footerY: 130);

        Assert.False(result);
    }
}
