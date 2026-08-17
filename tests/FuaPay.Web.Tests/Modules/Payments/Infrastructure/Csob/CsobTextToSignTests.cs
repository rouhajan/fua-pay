using System.Text;

using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobTextToSignTests
{
    [Fact]
    public void Echo_UsesMerchantAndTimestampOnly()
    {
        Assert.Equal(
            "M1MIPS0000|20220125131559",
            CsobTextToSign.Echo(
                "M1MIPS0000",
                "20220125131559"));
    }

    [Fact]
    public void PaymentInit_UsesSpecificationOrderAndUtf8Values()
    {
        var merchantData = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("payment:8f31"));

        var result = CsobTextToSign.PaymentInit(
            "M1MIPS0000",
            "5547",
            "20220125131559",
            123400,
            new Uri("https://shop.example.com/return"),
            [
                new CsobPaymentCartItem(
                    "Dobití kreditu",
                    1,
                    123400)
            ],
            merchantData,
            900);

        Assert.Equal(
            "M1MIPS0000|5547|20220125131559|payment|card|123400|CZK|true|" +
            "https://shop.example.com/return|POST|Dobití kreditu|1|123400|" +
            $"{merchantData}|cs|900",
            result);
    }

    [Fact]
    public void PaymentInit_OmitsAbsentCartDescriptionWithoutEmptyDelimiter()
    {
        var merchantData = Convert.ToBase64String([1, 2, 3]);

        var result = CsobTextToSign.PaymentInit(
            "M1MIPS0000",
            "7",
            "20220125131559",
            1000,
            new Uri("https://shop.example.com/return"),
            [new CsobPaymentCartItem("Kredit", 1, 1000)],
            merchantData,
            300);

        Assert.DoesNotContain("1000||", result, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentInit_RejectsCartTotalMismatch()
    {
        Assert.Throws<ArgumentException>(
            () => CsobTextToSign.PaymentInit(
                "M1MIPS0000",
                "7",
                "20220125131559",
                1000,
                new Uri("https://shop.example.com/return"),
                [new CsobPaymentCartItem("Kredit", 1, 999)],
                Convert.ToBase64String([1]),
                300));
    }

    [Fact]
    public void PaymentInit_NormalizesCartBeforeBuildingSignature()
    {
        var merchantData = Convert.ToBase64String([1, 2, 3]);

        var result = CsobTextToSign.PaymentInit(
            "M1MIPS0000",
            "7",
            "20220125131559",
            1000,
            new Uri("https://shop.example.com/return"),
            [new CsobPaymentCartItem("  Kredit  ", 1, 1000, "   ")],
            merchantData,
            300);

        Assert.Equal(
            "M1MIPS0000|7|20220125131559|payment|card|1000|CZK|true|" +
            "https://shop.example.com/return|POST|Kredit|1|1000|" +
            $"{merchantData}|cs|300",
            result);
    }

    [Theory]
    [InlineData("Kredit|zakázka")]
    [InlineData("Kredit\u2028zakázka")]
    public void PaymentInit_RejectsAmbiguousOrControlCartText(
        string name)
    {
        Assert.Throws<ArgumentException>(
            () => CsobTextToSign.PaymentInit(
                "M1MIPS0000",
                "7",
                "20220125131559",
                1000,
                new Uri("https://shop.example.com/return"),
                [new CsobPaymentCartItem(name, 1, 1000)],
                Convert.ToBase64String([1]),
                300));
    }


    [Fact]
    public void PaymentProcess_UsesDecodedValuesInSpecificationOrder()
    {
        Assert.Equal(
            "M1MIPS0000|ff41e84b7e33@HA|20220125131559",
            CsobTextToSign.PaymentProcess(
                "M1MIPS0000",
                "ff41e84b7e33@HA",
                "20220125131559"));
    }

    [Fact]
    public void PaymentStatus_UsesDecodedValuesInSpecificationOrder()
    {
        Assert.Equal(
            "M1MIPS0000|ff41e84b7e33@HA|20220125131559",
            CsobTextToSign.PaymentStatus(
                "M1MIPS0000",
                "ff41e84b7e33@HA",
                "20220125131559"));
    }
}
