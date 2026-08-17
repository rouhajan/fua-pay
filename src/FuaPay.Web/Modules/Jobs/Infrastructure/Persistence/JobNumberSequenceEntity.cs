namespace FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;

internal sealed class JobNumberSequenceEntity
{
    public Guid ServiceUnitId { get; set; }

    public int Year { get; set; }

    public int LastValue { get; set; }
}
