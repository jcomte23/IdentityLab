namespace IdentityLab.Mvc.Services;

public interface IRazorViewRenderer
{
    // Renders a Razor view (e.g. "/Views/Emails/ConfirmEmail.cshtml") to an HTML string.
    Task<string> RenderAsync<TModel>(string viewPath, TModel model);
}
