using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace DomoLibri.Tests.Infrastructure;

public class ConsoleEmailServiceTests
{
    private static ConsoleEmailService CreateSut()
    {
        var logger = new Mock<ILogger<ConsoleEmailService>>();
        return new ConsoleEmailService(logger.Object);
    }

    [Fact]
    public async Task SendEmailAsync_CompletesSuccessfully()
    {
        var sut = CreateSut();
        await sut.SendEmailAsync("to@test.com", "Assunto", "Corpo do email");
    }

    [Fact]
    public async Task SendEmailAsync_ReturnsCompletedTask()
    {
        var loggerMock = new Mock<ILogger<ConsoleEmailService>>();
        var sut = new ConsoleEmailService(loggerMock.Object);

        var task = sut.SendEmailAsync("to@test.com", "Assunto", "Corpo");
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SendEmailAsync_LogsInformation()
    {
        var loggerMock = new Mock<ILogger<ConsoleEmailService>>();
        var sut = new ConsoleEmailService(loggerMock.Object);

        await sut.SendEmailAsync("to@test.com", "Assunto", "Corpo");

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendVerificationEmailAsync_CompletesSuccessfully()
    {
        var sut = CreateSut();
        await sut.SendVerificationEmailAsync("to@test.com", "Usuário", "https://link.com/verify");
    }

    [Fact]
    public async Task SendVerificationEmailAsync_ReturnsCompletedTask()
    {
        var loggerMock = new Mock<ILogger<ConsoleEmailService>>();
        var sut = new ConsoleEmailService(loggerMock.Object);

        var task = sut.SendVerificationEmailAsync("to@test.com", "Usuário", "https://link.com/verify");
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SendVerificationEmailAsync_LogsInformation()
    {
        var loggerMock = new Mock<ILogger<ConsoleEmailService>>();
        var sut = new ConsoleEmailService(loggerMock.Object);

        await sut.SendVerificationEmailAsync("to@test.com", "Usuário", "https://link.com/verify");

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_CompletesSuccessfully()
    {
        var sut = CreateSut();
        await sut.SendPasswordResetEmailAsync("to@test.com", "Usuário", "https://link.com/reset");
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_ReturnsCompletedTask()
    {
        var loggerMock = new Mock<ILogger<ConsoleEmailService>>();
        var sut = new ConsoleEmailService(loggerMock.Object);

        var task = sut.SendPasswordResetEmailAsync("to@test.com", "Usuário", "https://link.com/reset");
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_LogsInformation()
    {
        var loggerMock = new Mock<ILogger<ConsoleEmailService>>();
        var sut = new ConsoleEmailService(loggerMock.Object);

        await sut.SendPasswordResetEmailAsync("to@test.com", "Usuário", "https://link.com/reset");

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConsoleEmailService_ImplementsIEmailService()
    {
        var sut = CreateSut();
        Assert.IsAssignableFrom<IEmailService>(sut);
    }
}
