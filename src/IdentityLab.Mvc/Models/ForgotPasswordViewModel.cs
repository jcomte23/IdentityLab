using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.Models;

public class ForgotPasswordViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}