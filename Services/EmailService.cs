using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using System.Threading;

namespace EventLauscherApi.Services
{
    public interface IEmailService
    {
        Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    }

    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _cfg;
        public SmtpEmailService(IConfiguration cfg) => _cfg = cfg;

        public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(_cfg["Email:FromName"], _cfg["Email:From"]));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = subject;
            msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_cfg["Email:SmtpHost"], int.Parse(_cfg["Email:SmtpPort"]!), SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_cfg["Email:User"], _cfg["Email:Password"], ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
        }
    }
}
