using System.Net.Mail;
using FlashSeat.Identity.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashSeat.Identity.Infrastructure;

public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendVerificationEmailAsync(string toEmail, string fullName, string code, CancellationToken cancellationToken = default)
    {
        try
        {
            // ponytail: Simple SmtpClient for Mailpit/Resend. Add retry queue / outbox worker when moving beyond SMTP relay.
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl
            };

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                client.UseDefaultCredentials = false;
                client.Credentials = new System.Net.NetworkCredential(_options.Username, _options.Password);
            }

            var message = new MailMessage(_options.From, toEmail)
            {
                Subject = "Verify your FlashSeat account",
                Body = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 480px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background: #ffffff;"">
    <h2 style=""color: #2563eb; margin: 0 0 16px; font-size: 24px;"">FlashSeat</h2>
    <p style=""font-size: 15px; color: #1e293b; line-height: 1.5;"">Hello {fullName},</p>
    <p style=""font-size: 15px; color: #1e293b; line-height: 1.5;"">Your email verification code for FlashSeat is:</p>
    <div style=""background: #f1f5f9; padding: 18px; text-align: center; border-radius: 6px; margin: 24px 0;"">
        <span style=""font-family: Consolas, Monaco, monospace; font-size: 32px; font-weight: 700; letter-spacing: 6px; color: #0f172a;"">{code}</span>
    </div>
    <p style=""font-size: 13px; color: #64748b; line-height: 1.4;"">This code will expire in 15 minutes. If you did not request this registration, please ignore this email.</p>
</div>",
                IsBodyHtml = true
            };

            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", toEmail);
        }
    }
}
