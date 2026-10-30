using EventLauscherApi.Services;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Moq;

namespace EventLauscherApi.Tests.Services;

public sealed class EmailServiceTests
{
    [Fact]
    public async Task SendBuildsHtmlMessageUsesStartTlsAuthenticatesAndDisposesClient()
    {
        var client = new Mock<ISmtpClient>(MockBehavior.Strict);
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        var sequence = new MockSequence();
        client.InSequence(sequence).Setup(x => x.ConnectAsync("smtp.example.test", 587, SecureSocketOptions.StartTls, ct)).Returns(Task.CompletedTask);
        client.InSequence(sequence).Setup(x => x.AuthenticateAsync("smtp-user", "smtp-password", ct)).Returns(Task.CompletedTask);
        MimeMessage? sent = null;
        client.InSequence(sequence).Setup(x => x.SendAsync(It.IsAny<MimeMessage>(), ct, null))
            .Callback<MimeMessage, CancellationToken, ITransferProgress?>((message, _, _) => sent = message)
            .ReturnsAsync("queued");
        client.InSequence(sequence).Setup(x => x.DisconnectAsync(true, ct)).Returns(Task.CompletedTask);
        client.Setup(x => x.Dispose());
        await new TestEmailService(Configuration(), client.Object).SendAsync("alice@example.com", "Subject", "<p>Hello</p>", ct);
        Assert.NotNull(sent);
        Assert.Equal("events@example.com", Assert.Single(sent.From.Mailboxes).Address);
        Assert.Equal("Eventlauscher", Assert.Single(sent.From.Mailboxes).Name);
        Assert.Equal("alice@example.com", Assert.Single(sent.To.Mailboxes).Address);
        Assert.Equal("Subject", sent.Subject);
        Assert.Equal("<p>Hello</p>", sent.HtmlBody);
        client.VerifyAll();
        client.Verify(x => x.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("authenticate")]
    [InlineData("send")]
    public async Task SmtpFailuresPropagateAndAlwaysDisposeClient(string stage)
    {
        var client = new Mock<ISmtpClient>();
        client.Setup(x => x.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()))
            .Returns(stage == "connect" ? Task.FromException(new IOException("SMTP failed")) : Task.CompletedTask);
        client.Setup(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(stage == "authenticate" ? Task.FromException(new IOException("SMTP failed")) : Task.CompletedTask);
        client.Setup(x => x.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), null))
            .Returns(stage == "send" ? Task.FromException<string>(new IOException("SMTP failed")) : Task.FromResult("queued"));
        await Assert.ThrowsAsync<IOException>(() => new TestEmailService(Configuration(), client.Object).SendAsync("alice@example.com", "Subject", "<p>Hello</p>"));
        client.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public async Task InvalidRecipientIsRejectedBeforeConnecting()
    {
        var client = new Mock<ISmtpClient>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ParseException>(() => new TestEmailService(Configuration(), client.Object).SendAsync("invalid address", "Subject", "body"));
        client.VerifyNoOtherCalls();
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Email:FromName"] = "Eventlauscher", ["Email:From"] = "events@example.com",
        ["Email:SmtpHost"] = "smtp.example.test", ["Email:SmtpPort"] = "587",
        ["Email:User"] = "smtp-user", ["Email:Password"] = "smtp-password"
    }).Build();

    private sealed class TestEmailService(IConfiguration configuration, ISmtpClient client) : SmtpEmailService(configuration)
    {
        protected override ISmtpClient CreateClient() => client;
    }
}
