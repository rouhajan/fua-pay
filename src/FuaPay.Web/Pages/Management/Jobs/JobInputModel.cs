using System.ComponentModel.DataAnnotations;

using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Pages.Shared;

namespace FuaPay.Web.Pages.Management.Jobs;

public sealed class JobInputModel
{
    [Required(ErrorMessage = "Vyberte službu.")]
    public Guid? ServiceUnitId { get; set; }

    [Required(ErrorMessage = "Vyberte zákazníka.")]
    public Guid? CustomerUserId { get; set; }

    public ServiceType ServiceType { get; set; }

    [Required(ErrorMessage = "Zadejte název zakázky.")]
    [StringLength(
        JobTextLimits.TitleMaxLength,
        ErrorMessage = "Název může mít nejvýše 200 znaků.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Zadejte popis zakázky.")]
    [StringLength(
        JobTextLimits.DescriptionMaxLength,
        ErrorMessage = "Popis může mít nejvýše 4 000 znaků.")]
    public string Description { get; set; } = string.Empty;

    [FinancialAmountRange(
        FinancialAmountKind.JobPrice,
        ErrorMessage = "Cena musí být mezi 0,01 Kč a 1 000 000 Kč.")]
    public decimal PriceCrowns { get; set; }
}
