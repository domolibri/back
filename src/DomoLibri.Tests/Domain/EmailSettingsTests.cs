using DomoLibri.Domain.Settings;

namespace DomoLibri.Tests.Domain;

public class EmailSettingsTests
{
    [Fact]
    public void EmailSettings_DefaultValues_AreCorrect()
    {
        var settings = new EmailSettings();

        Assert.Equal(string.Empty, settings.SmtpServer);
        Assert.Equal(587, settings.SmtpPort);
        Assert.False(settings.UseSsl);
        Assert.Equal(string.Empty, settings.Username);
        Assert.Equal(string.Empty, settings.Password);
        Assert.Equal(string.Empty, settings.SenderName);
        Assert.Equal(string.Empty, settings.SenderEmail);
    }

    [Fact]
    public void EmailSettings_PropertiesCanBeSet()
    {
        var settings = new EmailSettings
        {
            SmtpServer = "smtp.example.com",
            SmtpPort = 465,
            UseSsl = true,
            Username = "no-reply@example.com",
            Password = "secret",
            SenderName = "DomoLibri",
            SenderEmail = "no-reply@domolibri.com.br"
        };

        Assert.Equal("smtp.example.com", settings.SmtpServer);
        Assert.Equal(465, settings.SmtpPort);
        Assert.True(settings.UseSsl);
        Assert.Equal("no-reply@example.com", settings.Username);
        Assert.Equal("secret", settings.Password);
        Assert.Equal("DomoLibri", settings.SenderName);
        Assert.Equal("no-reply@domolibri.com.br", settings.SenderEmail);
    }

    [Fact]
    public void EmailSettings_PortDefault_Is587()
    {
        var settings = new EmailSettings();
        Assert.Equal(587, settings.SmtpPort);
    }

    [Fact]
    public void EmailSettings_UseSslDefault_IsFalse()
    {
        var settings = new EmailSettings();
        Assert.False(settings.UseSsl);
    }
}
