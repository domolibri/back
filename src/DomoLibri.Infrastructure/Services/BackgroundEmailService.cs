using DomoLibri.Domain.Interfaces;
using Hangfire;

namespace DomoLibri.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IEmailService"/> by enqueuing fire-and-forget Hangfire jobs.
/// The actual SMTP work is performed by <see cref="SmtpEmailService"/> resolved by Hangfire's DI activator.
/// </summary>
public class BackgroundEmailService : IEmailService
{
    private readonly IBackgroundJobClient _jobClient;

    public BackgroundEmailService(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public Task SendEmailAsync(string toEmail, string subject, string body)
    {
        _jobClient.Enqueue<SmtpEmailService>(s => s.SendEmailAsync(toEmail, subject, body));
        return Task.CompletedTask;
    }

    public Task SendVerificationEmailAsync(string toEmail, string userName, string verificationLink)
    {
        _jobClient.Enqueue<SmtpEmailService>(s => s.SendVerificationEmailAsync(toEmail, userName, verificationLink));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink)
    {
        _jobClient.Enqueue<SmtpEmailService>(s => s.SendPasswordResetEmailAsync(toEmail, userName, resetLink));
        return Task.CompletedTask;
    }
}
