namespace IdentityLab.Mvc.Services;

public interface IEmailSender
{
    // textBody is the plain-text alternative shown by clients that don't render HTML.
    Task SendEmailAsync(string to, string subject, string htmlBody, string? textBody = null);
}
