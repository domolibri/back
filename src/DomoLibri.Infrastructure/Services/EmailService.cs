using DomoLibri.Domain.Interfaces;
using DomoLibri.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Net;

namespace DomoLibri.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;

    // Recebe as configurações via Injeção de Dependência (Options Pattern)
    public EmailService(IOptions<EmailSettings> emailSettings)
    {
        _emailSettings = emailSettings.Value;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
        message.To.Add(new MailboxAddress("", toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"<div style='font-family: Arial, sans-serif; padding: 20px; color: #201F1E;'>{body}</div>",
            TextBody = body
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var socketOptions = _emailSettings.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, socketOptions);

            if (!string.IsNullOrWhiteSpace(_emailSettings.Username))
                await client.AuthenticateAsync(_emailSettings.Username, _emailSettings.Password);

            await client.SendAsync(message);
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    public async Task SendVerificationEmailAsync(string toEmail, string userName, string verificationLink)
    {
        var message = new MimeMessage();

        // Remetente (Domo Libri)
        message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));

        // Destinatário (Novo usuário da editora)
        message.To.Add(new MailboxAddress(userName, toEmail));

        message.Subject = "Confirme seu e-mail no Domo Libri";

        // Security: HTML Encode user-provided input to prevent XSS in email clients
        var encodedName = WebUtility.HtmlEncode(userName);

        // Corpo do e-mail (Suporta HTML e Texto Puro)
        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #201F1E;'>
                    <h2>Bem-vindo(a) ao Domo Libri, {encodedName}!</h2>
                    <p>Estamos quase lá. Para acessar o dashboard da sua editora, por favor, confirme seu endereço de e-mail clicando no botão abaixo:</p>
                    <a href='{verificationLink}' style='display: inline-block; padding: 10px 20px; background-color: #0078D4; color: #FFFFFF; text-decoration: none; border-radius: 5px; font-weight: bold;'>Confirmar Meu E-mail</a>
                    <p style='margin-top: 20px; font-size: 12px; color: #605E5C;'>Se o botão não funcionar, copie e cole o seguinte link no seu navegador:<br>{verificationLink}</p>
                </div>",
            TextBody = $"Bem-vindo(a) ao Domo Libri, {userName}!\n\nPara acessar seu dashboard, confirme seu e-mail acessando o link: {verificationLink}"
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();

        try
        {
            var socketOptions = _emailSettings.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, socketOptions);

            if (!string.IsNullOrWhiteSpace(_emailSettings.Username))
                await client.AuthenticateAsync(_emailSettings.Username, _emailSettings.Password);

            await client.SendAsync(message);
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetLink)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
        message.To.Add(new MailboxAddress(userName, toEmail));
        message.Subject = "Redefinição de senha - Domo Libri";

        var encodedName = WebUtility.HtmlEncode(userName);

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #201F1E;'>
                    <h2>Redefinição de senha, {encodedName}</h2>
                    <p>Recebemos uma solicitação para redefinir a senha da sua conta. Clique no botão abaixo para criar uma nova senha. O link é válido por <strong>1 hora</strong>.</p>
                    <a href='{resetLink}' style='display: inline-block; padding: 10px 20px; background-color: #0078D4; color: #FFFFFF; text-decoration: none; border-radius: 5px; font-weight: bold;'>Redefinir Minha Senha</a>
                    <p style='margin-top: 20px; font-size: 12px; color: #605E5C;'>Se você não solicitou a redefinição, ignore este e-mail. Sua senha permanece a mesma.<br><br>Se o botão não funcionar, copie e cole o link no seu navegador:<br>{resetLink}</p>
                </div>",
            TextBody = $"Redefinição de senha, {userName}!\n\nAcesse o link abaixo para criar uma nova senha (válido por 1 hora):\n{resetLink}\n\nSe você não solicitou, ignore este e-mail."
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();

        try
        {
            var socketOptions = _emailSettings.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, socketOptions);

            if (!string.IsNullOrWhiteSpace(_emailSettings.Username))
                await client.AuthenticateAsync(_emailSettings.Username, _emailSettings.Password);

            await client.SendAsync(message);
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }
}