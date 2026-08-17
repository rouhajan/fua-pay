using System.Net;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public class CsobGatewayException : Exception
{
    public CsobGatewayException(
        string message,
        int? resultCode = null,
        HttpStatusCode? httpStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ResultCode = resultCode;
        HttpStatusCode = httpStatusCode;
    }

    public int? ResultCode { get; }

    public HttpStatusCode? HttpStatusCode { get; }
}
