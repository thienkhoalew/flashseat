using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlashSeat.Payment.Application;
using Microsoft.Extensions.Options;

namespace FlashSeat.Payment.Infrastructure;

public interface IPayOSClient
{
    Task<PayOSPaymentData> CreatePaymentLinkAsync(PayOSPaymentRequest request, CancellationToken cancellationToken);
    Task<PayOSPaymentData?> GetPaymentLinkAsync(long orderCode, long amount, CancellationToken cancellationToken);
    Task CancelPaymentLinkAsync(string paymentLinkId, string reason, CancellationToken cancellationToken);
}

public sealed class PayOSClient(HttpClient httpClient, IOptions<PayOSOptions> options) : IPayOSClient
{
    private readonly PayOSOptions options = options.Value;

    public async Task<PayOSPaymentData> CreatePaymentLinkAsync(PayOSPaymentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.ChecksumKey))
            throw new InvalidOperationException("PayOS credentials are not configured.");

        using var message = new HttpRequestMessage(HttpMethod.Post, "v2/payment-requests")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        AddAuthenticationHeaders(message);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var payload = await ReadResponseAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode || payload?.Code != "00" || payload.Data is null)
            throw new HttpRequestException($"PayOS payment link creation failed with code {payload?.Code ?? response.StatusCode.ToString()}: {payload?.Desc ?? response.ReasonPhrase}");

        return ValidatePaymentData(payload.Data, request.OrderCode, request.Amount);
    }

    public async Task<PayOSPaymentData?> GetPaymentLinkAsync(long orderCode, long amount, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("PayOS credentials are not configured.");

        using var message = new HttpRequestMessage(HttpMethod.Get, $"v2/payment-requests/{orderCode}");
        AddAuthenticationHeaders(message);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        var payload = await ReadResponseAsync(response, cancellationToken);
        if (payload?.Code == "1611") return null;
        if (!response.IsSuccessStatusCode || payload?.Code != "00" || payload.Data is null)
            throw new HttpRequestException($"PayOS payment link lookup failed with code {payload?.Code ?? response.StatusCode.ToString()}: {payload?.Desc ?? response.ReasonPhrase}");

        return ValidatePaymentData(payload.Data, orderCode, amount);
    }

    public async Task CancelPaymentLinkAsync
        (string paymentLinkId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("PayOS credentials are not configured.");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"v2/payment-requests/{Uri.EscapeDataString(paymentLinkId)}/cancel")
        {
            Content = JsonContent.Create(new { cancellationReason = reason }, options: JsonOptions)
        };
        AddAuthenticationHeaders(message);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"PayOS payment link cancellation failed: {response.StatusCode}", null, response.StatusCode);
    }

    private static PayOSPaymentData ValidatePaymentData(PayOSPaymentData data, long orderCode, long amount)
    {
        var invalidFields = new List<string>();
        if (data.OrderCode != orderCode) invalidFields.Add("orderCode");
        if (data.Amount != amount) invalidFields.Add("amount");
        if (!string.Equals(data.Currency, "VND", StringComparison.OrdinalIgnoreCase)) invalidFields.Add("currency");
        if (string.IsNullOrWhiteSpace(data.PaymentLinkId)) invalidFields.Add("paymentLinkId");
        if (string.IsNullOrWhiteSpace(data.CheckoutUrl)) invalidFields.Add("checkoutUrl");
        if (string.IsNullOrWhiteSpace(data.QrCode)) invalidFields.Add("qrCode");
        if (invalidFields.Count > 0)
            throw new HttpRequestException($"PayOS returned an invalid payment link for order {orderCode}; invalid fields: {string.Join(", ", invalidFields)}.");
        return data;
    }

    private void AddAuthenticationHeaders(HttpRequestMessage message)
    {
        message.Headers.TryAddWithoutValidation("x-client-id", options.ClientId);
        message.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
    }

    private static async Task<PayOSPaymentResponse?> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<PayOSPaymentResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException($"PayOS returned an invalid JSON response ({exception.Message}).", exception, response.StatusCode);
        }
    }

    public static string CreatePaymentSignature(PayOSPaymentRequest request, string checksumKey)
    {
        var canonical = string.Join('&',
            $"amount={request.Amount}",
            $"cancelUrl={request.CancelUrl}",
            $"description={request.Description}",
            $"orderCode={request.OrderCode}",
            $"returnUrl={request.ReturnUrl}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(checksumKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
