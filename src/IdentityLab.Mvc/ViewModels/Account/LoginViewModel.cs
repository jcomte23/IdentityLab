using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.ViewModels.Account;

public class LoginViewModel
{
    [Required]
    [Display(Name = "Email or username")]
    public string EmailOrUserName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}