using System.ComponentModel.DataAnnotations;

namespace IdentityLab.Mvc.Models;

public class EnableAuthenticatorViewModel
{
    public string SharedKey { get; set; } = string.Empty;

    // PNG image as a data URI ("data:image/png;base64,...") ready for an <img src>.
    public string QrCodeImage { get; set; } = string.Empty;

    [Required]
    [StringLength(7, MinimumLength = 6)]
    [Display(Name = "Verification code")]
    public string Code { get; set; } = string.Empty;
}
