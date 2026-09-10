namespace FlashSeat.Identity.Application;

public interface IEmailSender
{
    Task SendVerificationEmailAsync(string toEmail, string fullName, string code, CancellationToken cancellationToken = default);
}
