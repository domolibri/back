using DomoLibri.Domain.Interfaces;
using DomoLibri.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DomoLibri.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;

    // Recebe as configurações via Injeção de Dependência (Options Pattern)
    public EmailService(IOptions<EmailSettings> emailSettings)
    {
        _emailSettings = emailSettings.Value;
    }

    public async Task SendVerificationEmailAsync(string toEmail, string userName, string verificationLink)
    {
        var message = new MimeMessage();

        // Remetente (Domo Libri)
        message.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));

        // Destinatário (Novo usuário da editora)
        message.To.Add(new MailboxAddress(userName, toEmail));

        message.Subject = "Confirme seu e-mail no Domo Libri";

        // Corpo do e-mail (Suporta HTML e Texto Puro)
        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #201F1E;'>
                    <h2>Bem-vindo(a) ao Domo Libri, {userName}!</h2>
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

            Console.WriteLine($"Conectando ao servidor SMTP: {_emailSettings.SmtpServer}:{_emailSettings.SmtpPort} com SSL: {_emailSettings.UseSsl}");
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