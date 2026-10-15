using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.Models;

public class LoginWithRecoveryCodeViewModel
{
    [Required]
    [Display(Name = "Recovery code")]
    public string RecoveryCode { get; set; } = string.Empty;
}
