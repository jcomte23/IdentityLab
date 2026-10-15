using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.ViewModels.Account;

public class ForgotPasswordViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}