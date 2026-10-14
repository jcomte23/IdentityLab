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

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName
        };

        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var confirmationLink = Url.Action(
                "ConfirmEmail",
                "Account",
                new { userId = user.Id, code = encodedToken },
                Request.Scheme) ?? throw new InvalidOperationException("Could not generate the email confirmation link.");


            await _emailSender.SendEmailAsync(
                model.Email,
                "Confirm your email",
                $"<p>Please confirm your account by <a href=\"{HtmlEncoder.Default.Encode(confirmationLink)}\">clicking here</a>.</p>");
            
            return RedirectToAction("RegisterConfirmation");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
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
        

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);
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

        ModelState.AddModelError(string.Empty, "Invalid login attempt. If you just registered, make sure you have confirmed your email.");
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
    public IActionResult RegisterConfirmation()
    {
        return View();
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