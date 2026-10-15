using IdentityLab.Mvc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace IdentityLab.Mvc.Controllers;

[Authorize]
public class ManageController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ManageController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    /*--------------------------------*/
    /*Resumen de la cuenta del usuario*/
    /*--------------------------------*/
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            // The cookie is valid but the user no longer exists (e.g. it was deleted).
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        var logins = await _userManager.GetLoginsAsync(user);

        var model = new ManageIndexViewModel
        {
            FullName = user.FullName,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            EmailConfirmed = user.EmailConfirmed,
            HasPassword = await _userManager.HasPasswordAsync(user),
            TwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user),
            RecoveryCodesLeft = await _userManager.CountRecoveryCodesAsync(user),
            ExternalLogins = logins.Select(l => l.ProviderDisplayName ?? l.LoginProvider).ToList()
        };

        return View(model);
    }

    /*-------------------------------------------*/
    /*Configurar la app autenticadora (2FA, TOTP)*/
    /*-------------------------------------------*/
    [HttpGet]
    public async Task<IActionResult> EnableAuthenticator()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction("Login", "Account");
        }

        var model = new EnableAuthenticatorViewModel();
        await LoadAuthenticatorSetupAsync(model, user);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnableAuthenticator(EnableAuthenticatorViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            await LoadAuthenticatorSetupAsync(model, user);
            return View(model);
        }

        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

        if (!isValid)
        {
            ModelState.AddModelError(nameof(model.Code), "The verification code is invalid. Check that the time on your phone is correct and try again.");
            await LoadAuthenticatorSetupAsync(model, user);
            return View(model);
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        await _signInManager.RefreshSignInAsync(user);

        TempData["StatusMessage"] = "Two-factor authentication is now enabled.";

        // Fresh codes every time 2FA is enabled, so codes from a previous setup stop working.
        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        TempData["RecoveryCodes"] = recoveryCodes?.ToArray();
        return RedirectToAction("ShowRecoveryCodes");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable2fa()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction("Login", "Account");
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        // New key: the entry in the authenticator app stops working and enabling again needs a new QR code.
        await _userManager.ResetAuthenticatorKeyAsync(user);
        // Forget "Remember this device" on this browser.
        await _signInManager.ForgetTwoFactorClientAsync();
        await _signInManager.RefreshSignInAsync(user);

        TempData["StatusMessage"] = "Two-factor authentication has been disabled. You can remove IdentityLab from your authenticator app.";
        return RedirectToAction("Index");
    }

    /*-------------------------------*/
    /*Códigos de recuperación del 2FA*/
    /*-------------------------------*/
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateRecoveryCodes()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction("Login", "Account");
        }

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
        {
            TempData["StatusMessage"] = "Enable two-factor authentication before generating recovery codes.";
            return RedirectToAction("Index");
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        TempData["RecoveryCodes"] = recoveryCodes?.ToArray();
        return RedirectToAction("ShowRecoveryCodes");
    }

    [HttpGet]
    public IActionResult ShowRecoveryCodes()
    {
        // The codes are only available right after generating them (TempData is read once).
        if (TempData["RecoveryCodes"] is not string[] recoveryCodes || recoveryCodes.Length == 0)
        {
            return RedirectToAction("Index");
        }

        return View(recoveryCodes);
    }

    /*------------------*/
    /*Eliminar la cuenta*/
    /*------------------*/
    [HttpGet]
    public async Task<IActionResult> DeleteAccount()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(new DeleteAccountViewModel { RequirePassword = await _userManager.HasPasswordAsync(user) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(DeleteAccountViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return RedirectToAction("Login", "Account");
        }

        // Never trust the form for this: check it again on the server.
        model.RequirePassword = await _userManager.HasPasswordAsync(user);

        if (model.RequirePassword &&
            (string.IsNullOrEmpty(model.Password) || !await _userManager.CheckPasswordAsync(user, model.Password)))
        {
            ModelState.AddModelError(nameof(model.Password), "Incorrect password.");
            return View(model);
        }

        if (!model.RequirePassword && model.Confirmation != user.UserName)
        {
            ModelState.AddModelError(nameof(model.Confirmation), "Type your username exactly to confirm.");
            return View(model);
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private async Task LoadAuthenticatorSetupAsync(EnableAuthenticatorViewModel model, ApplicationUser user)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        var email = await _userManager.GetEmailAsync(user) ?? user.UserName ?? string.Empty;

        model.SharedKey = FormatKey(key!);
        model.QrCodeImage = GenerateQrCodeImage(BuildAuthenticatorUri(email, key!));
    }

    // Standard format understood by authenticator apps (Google Authenticator, Microsoft Authenticator...).
    private static string BuildAuthenticatorUri(string email, string key)
    {
        const string issuer = "IdentityLab";
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}" +
               $"?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";
    }

    // Generated on the server so the secret key is never sent to a third-party service.
    private static string GenerateQrCodeImage(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    private static string FormatKey(string key)
    {
        var groups = Enumerable.Range(0, (key.Length + 3) / 4)
            .Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)));
        return string.Join(" ", groups).ToLowerInvariant();
    }
}
