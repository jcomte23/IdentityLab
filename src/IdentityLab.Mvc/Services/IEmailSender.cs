namespace IdentityLab.Mvc.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
}