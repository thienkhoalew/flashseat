namespace FlashSeat.Payment.Infrastructure;

public sealed class PayOSOptions
{
    public const string SectionName = "PayOS";
    public string ClientId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChecksumKey { get; set; } = string.Empty;
    public string BankId { get; set; } = "MB";
    public string AccountNumber { get; set; } = "0384064124";
    public string AccountName { get; set; } = "LE THIEN KHOA";
    public string BaseUrl { get; set; } = "https://api-merchant.payos.vn";
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
}
