using System.ComponentModel.DataAnnotations;

using FuaPay.Web.Pages.Management.Jobs;

namespace FuaPay.Web.Tests.Pages;

public sealed class JobInputModelTests
{
    [Fact]
    public void EmptyService_UsesUserFacingValidationMessage()
    {
        var input = CreateValidInput();
        input.ServiceUnitId = null;

        var errors = Validate(input);

        Assert.Contains("Vyberte službu.", errors);
        Assert.DoesNotContain(
            errors,
            message => message.Contains(
                "The value",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyCustomer_UsesUserFacingValidationMessage()
    {
        var input = CreateValidInput();
        input.CustomerUserId = null;

        Assert.Contains(
            "Vyberte zákazníka.",
            Validate(input));
    }

    private static JobInputModel CreateValidInput()
    {
        return new JobInputModel
        {
            ServiceUnitId = Guid.NewGuid(),
            CustomerUserId = Guid.NewGuid(),
            Title = "Testovací zakázka",
            Description = "Popis",
            PriceCrowns = 100m
        };
    }

    private static IReadOnlyList<string> Validate(
        JobInputModel input)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            input,
            new ValidationContext(input),
            results,
            validateAllProperties: true);

        return results
            .Select(item => item.ErrorMessage ?? string.Empty)
            .ToArray();
    }
}
