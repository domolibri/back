namespace DomoLibri.Domain.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string body);
    Task SendVerificationEmailAsync(string toEmail, string userName, string verificationLink);
    Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink);
}
