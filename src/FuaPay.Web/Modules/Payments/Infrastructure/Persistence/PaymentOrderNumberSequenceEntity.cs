namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class PaymentOrderNumberSequenceEntity
{
    public int Id { get; set; }

    public long LastValue { get; set; }
}
