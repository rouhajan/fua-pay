namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public interface ICsobGatewaySignature
{
    string Sign(string textToSign);

    bool Verify(string textToSign, string signature);
}
