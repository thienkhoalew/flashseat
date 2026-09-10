using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashSeat.Notification.Worker;

public interface IEmailSender
{
    Task SendEmailAsync(string toEmail, string toName, string subject, string body, bool isHtml, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendEmailAsync(string toEmail, string toName, string subject, string body, bool isHtml, CancellationToken cancellationToken = default)
    {
        try
        {
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
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };

            await client.SendMailAsync(message, cancellationToken);
            logger.LogInformation("Real email sent via SMTP to {ToEmail} with subject '{Subject}'", toEmail, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
            throw;
        }
    }
}
