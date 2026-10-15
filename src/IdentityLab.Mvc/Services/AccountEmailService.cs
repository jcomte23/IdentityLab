using IdentityLab.Mvc.ViewModels.Emails;

namespace IdentityLab.Mvc.Services;

public sealed class AccountEmailService(IRazorViewRenderer viewRenderer, IEmailSender emailSender) : IAccountEmailService
{
    public async Task SendEmailConfirmationAsync(string email, string displayName, string confirmationUrl)
    {
        var html = await viewRenderer.RenderAsync(
            "/Views/Emails/ConfirmEmail.cshtml",
            new ConfirmEmailModel(displayName, confirmationUrl));

        var text = $"""
            Hi {displayName},

            Thanks for signing up for IdentityLab. Confirm your email address by opening this link:

            {confirmationUrl}

            The link expires in 24 hours. If you didn't create an account, you can ignore this email.

            — The IdentityLab team
            """;

        await emailSender.SendEmailAsync(email, "Confirm your email address", html, text);
    }

    public async Task SendPasswordResetAsync(string email, string displayName, string resetUrl)
    {
        var html = await viewRenderer.RenderAsync(
            "/Views/Emails/ResetPassword.cshtml",
            new ResetPasswordEmailModel(displayName, resetUrl));

        var text = $"""
            Hi {displayName},

            We received a request to reset the password for your IdentityLab account. Choose a new password here:

            {resetUrl}

            The link works only once and expires in 24 hours.
            If you didn't ask to reset your password, you can ignore this email: your password won't change.

            — The IdentityLab team
            """;

        await emailSender.SendEmailAsync(email, "Reset your password", html, text);
    }
}
