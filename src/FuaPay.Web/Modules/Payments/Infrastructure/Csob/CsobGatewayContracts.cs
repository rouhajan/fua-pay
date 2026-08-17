using System.Text.Json.Serialization;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public sealed record CsobPaymentCartItem(
    string Name,
    int Quantity,
    long AmountMinorUnits,
    string? Description = null);

public sealed record CsobPaymentInit(
    string OrderNumber,
    long TotalAmountMinorUnits,
    IReadOnlyList<CsobPaymentCartItem> Cart,
    string MerchantData);

public sealed record CsobPaymentInitResult(
    string PayId,
    int PaymentStatus,
    Uri ProcessUri,
    int ResultCode = 0,
    string ResultMessage = "OK");

public sealed record CsobPaymentStatusResult(
    string PayId,
    int ResultCode,
    string ResultMessage,
    int PaymentStatus,
    string? AuthCode,
    string? StatusDetail);

public sealed record CsobEchoResult(
    int ResultCode,
    string ResultMessage);

internal sealed record CsobPaymentInitRequest(
    [property: JsonPropertyName("merchantId")] string MerchantId,
    [property: JsonPropertyName("orderNo")] string OrderNumber,
    [property: JsonPropertyName("dttm")] string Dttm,
    [property: JsonPropertyName("payOperation")] string PayOperation,
    [property: JsonPropertyName("payMethod")] string PayMethod,
    [property: JsonPropertyName("totalAmount")] long TotalAmount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("closePayment")] bool ClosePayment,
    [property: JsonPropertyName("returnUrl")] string ReturnUrl,
    [property: JsonPropertyName("returnMethod")] string ReturnMethod,
    [property: JsonPropertyName("cart")] IReadOnlyList<CsobPaymentCartItemRequest> Cart,
    [property: JsonPropertyName("merchantData")] string MerchantData,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("ttlSec")] int TtlSec,
    [property: JsonPropertyName("signature")] string Signature);

internal sealed record CsobPaymentCartItemRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("amount")] long Amount,
    [property: JsonPropertyName("description")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Description);

internal sealed record CsobGatewayResponse(
    [property: JsonPropertyName("payId")] string? PayId,
    [property: JsonPropertyName("dttm")] string? Dttm,
    [property: JsonPropertyName("resultCode")] int ResultCode,
    [property: JsonPropertyName("resultMessage")] string? ResultMessage,
    [property: JsonPropertyName("paymentStatus")] int? PaymentStatus,
    [property: JsonPropertyName("authCode")] string? AuthCode,
    [property: JsonPropertyName("merchantData")] string? MerchantData,
    [property: JsonPropertyName("statusDetail")] string? StatusDetail,
    [property: JsonPropertyName("signature")] string? Signature);

internal sealed record CsobGatewayErrorResponse(
    [property: JsonPropertyName("resultCode")] int? ResultCode,
    [property: JsonPropertyName("resultMessage")] string? ResultMessage);
