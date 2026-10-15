using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.ViewModels.Account;

public class LoginWithRecoveryCodeViewModel
{
    [Required]
    [Display(Name = "Recovery code")]
    public string RecoveryCode { get; set; } = string.Empty;
}
