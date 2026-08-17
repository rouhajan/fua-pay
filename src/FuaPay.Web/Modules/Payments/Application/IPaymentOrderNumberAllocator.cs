namespace FuaPay.Web.Modules.Payments.Application;

public interface IPaymentOrderNumberAllocator
{
    Task<long> AllocateAsync(
        CancellationToken cancellationToken = default);
}
