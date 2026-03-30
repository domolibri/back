namespace DomoLibri.Domain.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string toEmail, string userName, string verificationLink);
}
