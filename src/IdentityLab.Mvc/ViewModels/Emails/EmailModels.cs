namespace IdentityLab.Mvc.ViewModels.Emails;

// Data for the templates in Views/Emails.

public sealed record ConfirmEmailModel(string DisplayName, string ConfirmationUrl);

public sealed record ResetPasswordEmailModel(string DisplayName, string ResetUrl);

public sealed record EmailButtonModel(string Text, string Url);
