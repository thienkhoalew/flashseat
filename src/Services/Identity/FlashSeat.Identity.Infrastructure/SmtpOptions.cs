namespace FlashSeat.Identity.Infrastructure;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "noreply@flashseat.dev";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; }
}
