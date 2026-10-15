using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.Models;

public class EnableAuthenticatorViewModel
{
    public string SharedKey { get; set; } = string.Empty;

    [Required]
    [StringLength(7, MinimumLength = 6)]
    [Display(Name = "Verification code")]
    public string Code { get; set; } = string.Empty;
}
