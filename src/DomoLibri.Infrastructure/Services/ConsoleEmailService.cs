using DomoLibri.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DomoLibri.Infrastructure.Services;

public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string toEmail, string subject, string body)
    {
        _logger.LogInformation(
            "[EMAIL SIMULADO] Para: {Email} | Assunto: {Subject} | Corpo: {Body}",
            toEmail, subject, body);

        return Task.CompletedTask;
    }

    public Task SendVerificationEmailAsync(string toEmail, string userName, string verificationLink)
    {
        _logger.LogInformation(
            "[EMAIL SIMULADO] Para: {Email} | Usuário: {UserName} | Link de verificação: {Link}",
            toEmail, userName, verificationLink);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink)
    {
        _logger.LogInformation(
            "[EMAIL SIMULADO] Para: {Email} | Usuário: {UserName} | Link de redefinição de senha: {Link}",
            toEmail, userName, resetLink);

        return Task.CompletedTask;
    }
}
