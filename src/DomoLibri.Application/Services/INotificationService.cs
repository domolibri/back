namespace DomoLibri.Application.Services;

public interface INotificationService
{
    Task SendVerificationEmailAsync(string toEmail, string userName, string rawToken);
    Task SendPasswordResetEmailAsync(string toEmail, string userName, string rawToken);
    Task SendEditoraLinkedEmailAsync(string toEmail, string userName, string nomeEditora);
}
