using IdentityLab.Mvc.Services;
using IdentityLab.Mvc.ViewModels.Emails;
using Microsoft.AspNetCore.Mvc;

namespace IdentityLab.Mvc.Controllers;

/*-------------------------------------------------------*/
/*Vista previa de las plantillas de correo (solo en Dev)*/
/*-------------------------------------------------------*/
// Renders the email templates with sample data, without sending anything.
// Returns 404 outside the Development environment.
[Route("dev/emails")]
public class EmailPreviewController(IRazorViewRenderer viewRenderer, IWebHostEnvironment environment) : Controller
{
    private const string SampleUrl = "https://localhost:7083/Account/Sample?code=CfDJ8Example-Token_1234567890abcdefghijklmnopqrstuvwxyz";

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        return Content("""
            <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>Email previews</title></head>
            <body style="font-family: system-ui, sans-serif; padding: 2rem;">
            <h1>Email previews</h1>
            <ul>
              <li><a href="/dev/emails/confirm-email">Confirm email</a></li>
              <li><a href="/dev/emails/reset-password">Reset password</a></li>
            </ul>
            </body></html>
            """, "text/html");
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var html = await viewRenderer.RenderAsync("/Views/Emails/ConfirmEmail.cshtml", new ConfirmEmailModel("Jane Doe", SampleUrl));
        return Content(html, "text/html");
    }

    [HttpGet("reset-password")]
    public async Task<IActionResult> ResetPassword()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var html = await viewRenderer.RenderAsync("/Views/Emails/ResetPassword.cshtml", new ResetPasswordEmailModel("Jane Doe", SampleUrl));
        return Content(html, "text/html");
    }
}
