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
        message.Headers.TryAddWithoutValidation("x-client-id", options.ClientId);
        message.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<PayOSPaymentResponse>(JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode || payload?.Code != "00" || payload.Data is null)
            throw new HttpRequestException($"PayOS payment link creation failed with code {payload?.Code ?? response.StatusCode.ToString()}: {payload?.Desc ?? response.ReasonPhrase}");

        if (payload.Data.OrderCode != request.OrderCode || payload.Data.Amount != request.Amount || !string.Equals(payload.Data.Currency, "VND", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(payload.Data.PaymentLinkId) || string.IsNullOrWhiteSpace(payload.Data.CheckoutUrl))
            throw new HttpRequestException("PayOS returned an invalid payment link.");
        return payload.Data;
    }

    public async Task CancelPaymentLinkAsync(string paymentLinkId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("PayOS credentials are not configured.");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"v2/payment-requests/{Uri.EscapeDataString(paymentLinkId)}/cancel")
        {
            Content = JsonContent.Create(new { cancellationReason = reason }, options: JsonOptions)
        };
        message.Headers.TryAddWithoutValidation("x-client-id", options.ClientId);
        message.Headers.TryAddWithoutValidation("x-api-key", options.ApiKey);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"PayOS payment link cancellation failed: {response.StatusCode}", null, response.StatusCode);
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
