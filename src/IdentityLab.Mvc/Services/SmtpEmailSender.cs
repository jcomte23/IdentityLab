using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IdentityLab.Mvc.Services;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;

    public SmtpEmailSender(IOptions<SmtpSettings> options)
    {
        _settings = options.Value;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        // multipart/alternative: HTML for modern clients, plain text as a fallback (also helps with spam filters).
        var body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(_settings.UserName, _settings.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}