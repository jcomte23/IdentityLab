using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using IdentityLab.Mvc.Models;
using IdentityLab.Mvc.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace IdentityLab.Mvc.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailSender _emailSender;

    public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
        IEmailSender emailSender)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailSender = emailSender;
    }
    
    /*------------------------------*/
    /*Crear un usuario en el sistema*/
    /*------------------------------*/
    [HttpGet]
    public IActionResult Register()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new ApplicationUser(model.FullName,model.UserName, model.Email);

        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            // var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            // var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            // var confirmationLink = Url.Action(
            //     "ConfirmEmail",
            //     "Account",
            //     new { userId = user.Id, code = encodedToken },
            //     Request.Scheme) ?? throw new InvalidOperationException("Could not generate the email confirmation link.");
            //
            //
            // await _emailSender.SendEmailAsync(
            //     model.Email,
            //     "Confirm your email",
            //     $"<p>Please confirm your account by <a href=\"{HtmlEncoder.Default.Encode(confirmationLink)}\">clicking here</a>.</p>");
            
            return RedirectToAction("RegisterConfirmation");
        }

        
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }
    
    [HttpGet]
    public IActionResult RegisterConfirmation()
    {
        return View();
    }
    
    /*-----------------------------------------*/
    /*Login externo de un usuario en el sistema*/
    /*-----------------------------------------*/
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }
    
    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            ModelState.AddModelError(string.Empty, "Error loading external login information.");
            return View("Login", new LoginViewModel());
        }

        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (result.Succeeded)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut || result.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, "You can't log in with this account right now.");
            return View("Login", new LoginViewModel());
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (email is null)
        {
            ModelState.AddModelError(string.Empty, $"{info.LoginProvider} didn't share your email address.");
            return View("Login", new LoginViewModel());
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(string.Empty, "An account with this email already exists. Log in with your password.");
            return View("Login", new LoginViewModel { EmailOrUserName = email });
        }

        var user = new ApplicationUser(
            fullName: info.Principal.FindFirstValue(ClaimTypes.Name),
            userName: await GenerateUniqueUserNameAsync(email),
            email: email)
        {
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(user);
        if (createResult.Succeeded)
        {
            var loginResult = await _userManager.AddLoginAsync(user, info);
            if (loginResult.Succeeded)
            {
                await _signInManager.ExternalLoginSignInAsync(
                    info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

                if (Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            await _userManager.DeleteAsync(user);
            createResult = loginResult;
        }

        foreach (var error in createResult.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View("Login", new LoginViewModel());
    }
    
    private async Task<string> GenerateUniqueUserNameAsync(string email)
    {
        var localPart = email.Split('@')[0];
        var baseName = new string(localPart
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            .ToArray());

        if (baseName.Length < 3)
        {
            baseName = "user" + baseName;
        }

        var userName = baseName;
        var suffix = 1;
        while (await _userManager.FindByNameAsync(userName) is not null)
        {
            userName = $"{baseName}{suffix}";
            suffix++;
        }

        return userName;
    }


    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = model.EmailOrUserName.Contains('@')
            ? await _userManager.FindByEmailAsync(model.EmailOrUserName)
            : await _userManager.FindByNameAsync(model.EmailOrUserName);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt. If you just registered, make sure you have confirmed your email.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToAction("LoginWith2fa", new { returnUrl, rememberMe = model.RememberMe });
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked out. Please try again later.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt. If you just registered, make sure you have confirmed your email.");
        return View(model);
    }

    /*----------------------------------------------*/
    /*Segundo paso del login: código de la app (2FA)*/
    /*----------------------------------------------*/
    [HttpGet]
    public async Task<IActionResult> LoginWith2fa(bool rememberMe, string? returnUrl = null)
    {
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            // The temporary two-factor cookie is missing or expired: start over.
            return RedirectToAction("Login", new { returnUrl });
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginWith2faViewModel { RememberMe = rememberMe });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginWith2fa(LoginWith2faViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToAction("Login", new { returnUrl });
        }

        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
            code, model.RememberMe, model.RememberMachine);

        if (result.Succeeded)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked out. Please try again later.");
            return View(model);
        }

        ModelState.AddModelError(nameof(model.Code), "The code is invalid. Check that the time on your phone is correct and try again.");
        return View(model);
    }

    /*----------------------------------------------------*/
    /*Segundo paso del login: código de recuperación (2FA)*/
    /*----------------------------------------------------*/
    [HttpGet]
    public async Task<IActionResult> LoginWithRecoveryCode(string? returnUrl = null)
    {
        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToAction("Login", new { returnUrl });
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginWithRecoveryCode(LoginWithRecoveryCodeViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToAction("Login", new { returnUrl });
        }

        var recoveryCode = model.RecoveryCode.Replace(" ", string.Empty);
        var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

        if (result.Succeeded)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked out. Please try again later.");
            return View(model);
        }

        ModelState.AddModelError(nameof(model.RecoveryCode), "The recovery code is invalid or has already been used.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
    {
        if (userId is null || code is null)
        {
            return View(false);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return View(false);
        }

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        var result = await _userManager.ConfirmEmailAsync(user, token);

        return View(result.Succeeded);
    }
    
   
    
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View();
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null || !await _userManager.IsEmailConfirmedAsync(user))
        {
            return RedirectToAction("ForgotPasswordConfirmation");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var resetLink = Url.Action(
                            "ResetPassword",
                            "Account",
                            new { code = encodedToken },
                            Request.Scheme)
                        ?? throw new InvalidOperationException("Could not generate the password reset link.");

        await _emailSender.SendEmailAsync(
            model.Email,
            "Reset your password",
            $"<p>You can reset your password by <a href=\"{HtmlEncoder.Default.Encode(resetLink)}\">clicking here</a>.</p>" +
            "<p>If you didn't request this, you can ignore this email.</p>");

        return RedirectToAction("ForgotPasswordConfirmation");
    }
    
    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }
    
    [HttpGet]
    public IActionResult ResetPassword(string? code)
    {
        if (code is null)
        {
            return BadRequest("A code must be supplied for password reset.");
        }

        return View(new ResetPasswordViewModel { Code = code });
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, _userManager.ErrorDescriber.InvalidToken().Description);
            return View(model);
        }

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code));
        var result = await _userManager.ResetPasswordAsync(user, token, model.Password);

        if (result.Succeeded)
        {
            return RedirectToAction("ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }
    
    [HttpGet]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }
    
    
}