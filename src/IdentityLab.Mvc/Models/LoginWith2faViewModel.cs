using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.Models;

public class LoginWith2faViewModel
{
    [Required]
    [StringLength(7, MinimumLength = 6)]
    [Display(Name = "Authentication code")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Remember this device")]
    public bool RememberMachine { get; set; }

    public bool RememberMe { get; set; }
}
