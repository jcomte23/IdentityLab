using IdentityLab.Mvc.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

        return View(new EnableAuthenticatorViewModel { SharedKey = await GetOrCreateSharedKeyAsync(user) });
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
            model.SharedKey = await GetOrCreateSharedKeyAsync(user);
            return View(model);
        }

        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

        if (!isValid)
        {
            ModelState.AddModelError(nameof(model.Code), "The verification code is invalid. Check that the time on your phone is correct and try again.");
            model.SharedKey = await GetOrCreateSharedKeyAsync(user);
            return View(model);
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        await _signInManager.RefreshSignInAsync(user);

        TempData["StatusMessage"] = "Two-factor authentication is now enabled.";

        if (await _userManager.CountRecoveryCodesAsync(user) == 0)
        {
            var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            TempData["RecoveryCodes"] = recoveryCodes?.ToArray();
            return RedirectToAction("ShowRecoveryCodes");
        }

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

    private async Task<string> GetOrCreateSharedKeyAsync(ApplicationUser user)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        return FormatKey(key!);
    }

    private static string FormatKey(string key)
    {
        var groups = Enumerable.Range(0, (key.Length + 3) / 4)
            .Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)));
        return string.Join(" ", groups).ToLowerInvariant();
    }
}
