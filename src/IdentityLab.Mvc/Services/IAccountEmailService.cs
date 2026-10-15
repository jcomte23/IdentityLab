namespace IdentityLab.Mvc.Services;

// The account emails of the app. Controllers say *what* to send; this service knows *how* it looks.
public interface IAccountEmailService
{
    Task SendEmailConfirmationAsync(string email, string displayName, string confirmationUrl);

    Task SendPasswordResetAsync(string email, string displayName, string resetUrl);
}
