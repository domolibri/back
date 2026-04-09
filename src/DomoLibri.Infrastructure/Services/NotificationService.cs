using DomoLibri.Application.Services;
using DomoLibri.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DomoLibri.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;

    public NotificationService(IEmailService emailService, IConfiguration configuration)
    {
        _emailService = emailService;
        _configuration = configuration;
    }

    public Task SendVerificationEmailAsync(string toEmail, string userName, string rawToken)
    {
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var link = $"{frontendUrl}/onboarding/verify-email?email={Uri.EscapeDataString(toEmail)}&token={rawToken}";
        return _emailService.SendVerificationEmailAsync(toEmail, userName, link);
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string userName, string rawToken)
    {
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var link = $"{frontendUrl}/redefinir-senha?email={Uri.EscapeDataString(toEmail)}&token={rawToken}";
        return _emailService.SendPasswordResetEmailAsync(toEmail, userName, link);
    }

    public Task SendEditoraLinkedEmailAsync(string toEmail, string userName, string nomeEditora)
    {
        return _emailService.SendEmailAsync(toEmail, "Nova editora vinculada à sua conta",
            $"Olá {userName}, a editora '{nomeEditora}' foi vinculada à sua conta DomoLibri.");
    }
}
